using System.Buffers.Binary;
using System.IO.Compression;

namespace Console2Lce;

public sealed class SavegameProbeService
{
    public SavegameProbeResult Probe(
        ReadOnlyMemory<byte> savegameBytes,
        ReadOnlyMemory<byte> leadingPrefixBytes = default)
    {
        IReadOnlyList<SavegameEnvelopeCandidate> envelopes = SavegameEnvelopeReader.ReadCandidates(savegameBytes.Span, leadingPrefixBytes.Span);
        var findings = new List<string>();
        var attempts = new List<SavegameProbeAttempt>();
        byte[]? bestOutput = null;
        SavegameProbeAttempt? bestAttempt = null;
        string? leadingPrefixHex = leadingPrefixBytes.IsEmpty ? null : Convert.ToHexString(leadingPrefixBytes.Span);

        if (envelopes.Count == 0)
        {
            findings.Add("savegame.dat is smaller than the expected 8-byte save envelope.");
        }
        else if (!leadingPrefixBytes.IsEmpty)
        {
            findings.Add($"Probe included {leadingPrefixBytes.Length} prefix byte(s) recovered immediately before the computed first data block: {leadingPrefixHex}.");

            SavegameEnvelopeCandidate? recoveredBigEndian = envelopes.FirstOrDefault(candidate =>
                candidate.Name == "RecoveredPrefixHeaderBigEndian"
                && candidate.IsPlausible);

            if (recoveredBigEndian is not null)
            {
                findings.Add(
                    $"RecoveredPrefixHeaderBigEndian is plausible with compressionFlag=0 and expectedDecompressedSize={recoveredBigEndian.ExpectedDecompressedSize}, which matches the known big-endian Xbox 360 save platform.");
            }
        }

        foreach (SavegameEnvelopeCandidate envelope in envelopes)
        {
            if (!envelope.IsPlausible)
            {
                findings.Add(
                    $"{envelope.Name} is not plausible: compressionFlag={envelope.CompressionFlag}, expectedSize={envelope.ExpectedDecompressedSize}, payloadOffset={envelope.PayloadOffset}.");
                continue;
            }

            byte[] payload = savegameBytes.Span.Slice(envelope.PayloadOffset, envelope.PayloadLength).ToArray();
            foreach (DecoderCandidate decoder in GetDecoderCandidates())
            {
                SavegameProbeAttempt attempt = decoder.TryDecode(envelope, payload);
                attempts.Add(attempt);

                if (!attempt.Success || bestAttempt is not null)
                {
                    continue;
                }

                bestAttempt = attempt;
                bestOutput = decoder.Decode(envelope, payload);
            }
        }

        if (bestAttempt is null)
        {
            findings.Add("No candidate decompressor produced a plausible archive header.");
            findings.Add(
                XboxLzxNativeDecoder.AvailabilityFailure is null
                    ? "The native Xbox LZX helper is wired in, but the sample still does not decode into a plausible Xbox 360 4J archive. The remaining unknown is the exact outer save framing or decompression path before the archive header appears."
                    : "Xbox 360 saves likely use the native LZXRLE/XMem path, but the optional native helper is not available yet.");
        }

        SavegameProbeReport report = new(
            savegameBytes.Length,
            leadingPrefixHex,
            findings,
            envelopes,
            attempts,
            bestAttempt is not null,
            bestAttempt?.Envelope,
            bestAttempt?.Decoder);

        return new SavegameProbeResult(report, bestOutput);
    }

    private static IReadOnlyList<DecoderCandidate> GetDecoderCandidates()
    {
        return
        [
            new DecoderCandidate(
                "StoredWithoutCompression",
                static (envelope, payload) =>
                {
                    if (payload.Length != envelope.ExpectedDecompressedSize)
                    {
                        throw new SavegameDatDecompressionFailedException(
                            $"Payload length {payload.Length} does not match expected size {envelope.ExpectedDecompressedSize}.");
                    }

                    return payload.ToArray();
                }),
            new DecoderCandidate(
                "RleOnly",
                static (envelope, payload) => SavegameRleCodec.Decode(payload, envelope.ExpectedDecompressedSize)),
            new DecoderCandidate(
                "ZlibOnly",
                static (envelope, payload) =>
                {
                    byte[] decoded = ZlibDecompress(payload);
                    if (decoded.Length != envelope.ExpectedDecompressedSize)
                    {
                        throw new SavegameDatDecompressionFailedException(
                            $"Zlib decode produced {decoded.Length} bytes, expected {envelope.ExpectedDecompressedSize}.");
                    }

                    return decoded;
                }),
            new DecoderCandidate(
                "ZlibThenRle",
                static (envelope, payload) =>
                {
                    byte[] rleBytes = ZlibDecompress(payload);
                    return SavegameRleCodec.Decode(rleBytes, envelope.ExpectedDecompressedSize);
                }),
            new DecoderCandidate(
                "WindowsXpress",
                static (envelope, payload) => WindowsCompressionApi.Decompress(payload, envelope.ExpectedDecompressedSize, WindowsCompressionApi.CompressAlgorithmXpress)),
            new DecoderCandidate(
                "WindowsXpressRaw",
                static (envelope, payload) => WindowsCompressionApi.Decompress(payload, envelope.ExpectedDecompressedSize, WindowsCompressionApi.CompressAlgorithmXpress | WindowsCompressionApi.CompressRaw)),
            new DecoderCandidate(
                "WindowsXpressHuff",
                static (envelope, payload) => WindowsCompressionApi.Decompress(payload, envelope.ExpectedDecompressedSize, WindowsCompressionApi.CompressAlgorithmXpressHuff)),
            new DecoderCandidate(
                "WindowsXpressHuffRaw",
                static (envelope, payload) => WindowsCompressionApi.Decompress(payload, envelope.ExpectedDecompressedSize, WindowsCompressionApi.CompressAlgorithmXpressHuff | WindowsCompressionApi.CompressRaw)),
            new DecoderCandidate(
                "WindowsLzms",
                static (envelope, payload) => WindowsCompressionApi.Decompress(payload, envelope.ExpectedDecompressedSize, WindowsCompressionApi.CompressAlgorithmLzms)),
            new DecoderCandidate(
                "WindowsLzmsRaw",
                static (envelope, payload) => WindowsCompressionApi.Decompress(payload, envelope.ExpectedDecompressedSize, WindowsCompressionApi.CompressAlgorithmLzms | WindowsCompressionApi.CompressRaw)),
            new DecoderCandidate(
                "XboxLzxNativeThenRle128k128k",
                static (envelope, payload) => DecodeXboxLzxThenRle(payload, envelope.ExpectedDecompressedSize, 128 * 1024, 128 * 1024)),
            new DecoderCandidate(
                "XboxLzxNativeOnly128k128k",
                static (envelope, payload) => DecodeXboxLzxOnly(payload, envelope.ExpectedDecompressedSize, 128 * 1024, 128 * 1024)),
            new DecoderCandidate(
                "XboxLzxNativeThenRle128k512k",
                static (envelope, payload) => DecodeXboxLzxThenRle(payload, envelope.ExpectedDecompressedSize, 128 * 1024, 512 * 1024)),
            new DecoderCandidate(
                "XboxLzxNativeThenRleShift4_128k128k",
                static (envelope, payload) => DecodeXboxLzxThenRle(SlicePayload(payload, 4), envelope.ExpectedDecompressedSize, 128 * 1024, 128 * 1024)),
        ];
    }

    private static byte[] DecodeXboxLzxThenRle(byte[] payload, int expectedDecompressedSize, int windowSize, int partitionSize)
    {
        int intermediateBufferSize = XboxLzxNativeDecoder.GetRecommendedIntermediateBufferSize(expectedDecompressedSize);
        byte[] lzxBytes = XboxLzxNativeDecoder.Decompress(payload, intermediateBufferSize, windowSize, partitionSize);
        return SavegameRleCodec.Decode(lzxBytes, expectedDecompressedSize);
    }

    private static byte[] DecodeXboxLzxOnly(byte[] payload, int expectedDecompressedSize, int windowSize, int partitionSize)
    {
        int intermediateBufferSize = XboxLzxNativeDecoder.GetRecommendedIntermediateBufferSize(expectedDecompressedSize);
        return XboxLzxNativeDecoder.Decompress(payload, intermediateBufferSize, windowSize, partitionSize);
    }

    private static byte[] SlicePayload(byte[] payload, int offset)
    {
        if (payload.Length <= offset)
        {
            throw new SavegameDatDecompressionFailedException(
                $"Payload length {payload.Length} is too small for an additional {offset}-byte shift.");
        }

        return payload[offset..];
    }

    private static byte[] ZlibDecompress(byte[] payload)
    {
        using var input = new MemoryStream(payload, writable: false);
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static string ToHexPreview(ReadOnlySpan<byte> bytes, int byteCount)
    {
        int count = Math.Min(bytes.Length, byteCount);
        return Convert.ToHexString(bytes[..count]);
    }

    private sealed record DecoderCandidate(
        string Name,
        Func<SavegameEnvelopeCandidate, byte[], byte[]> DecodeCore)
    {
        public SavegameProbeAttempt TryDecode(SavegameEnvelopeCandidate envelope, byte[] payload)
        {
            try
            {
                byte[] decoded = Decode(envelope, payload);
                bool plausibleArchive = Minecraft360ArchiveParser.TryReadHeader(decoded, out Minecraft360ArchiveHeader header);

                if (!plausibleArchive)
                {
                    return new SavegameProbeAttempt(
                        envelope.Name,
                        Name,
                        false,
                        decoded.Length,
                        ToHexPreview(decoded, 16),
                        false,
                        null,
                        null,
                        "Decoded bytes did not produce a plausible Xbox 360 4J archive header.");
                }

                return new SavegameProbeAttempt(
                    envelope.Name,
                    Name,
                    true,
                    decoded.Length,
                    ToHexPreview(decoded, 16),
                    true,
                    header.HeaderOffset,
                    header.FileCount,
                    null);
            }
            catch (Exception exception)
            {
                return new SavegameProbeAttempt(
                    envelope.Name,
                    Name,
                    false,
                    null,
                    null,
                    false,
                    null,
                    null,
                    exception.Message);
            }
        }

        public byte[] Decode(SavegameEnvelopeCandidate envelope, byte[] payload)
        {
            return DecodeCore(envelope, payload);
        }
    }
}
