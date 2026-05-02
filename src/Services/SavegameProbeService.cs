using System.Buffers.Binary;
using System.IO.Compression;

namespace Console2Lce;

public sealed class SavegameProbeService : IDisposable
{
    private readonly Lazy<XboxXMemDecompressService> _xmem = new(() => new XboxXMemDecompressService());

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
                XboxXMemDecompressService.AvailabilityFailure is null
                    ? "XMemDecompress is available but the sample still does not decode into a plausible Xbox 360 4J archive."
                    : $"XMemDecompress is not available — cannot attempt Xbox 360 LZX decompression. {XboxXMemDecompressService.AvailabilityFailure}");
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

    private IReadOnlyList<DecoderCandidate> GetDecoderCandidates()
    {
        return
        [
            new DecoderCandidate(
                "StoredWithoutCompression",
                static (envelope, payload) =>
                {
                    if (payload.Length < envelope.ExpectedDecompressedSize)
                    {
                        throw new SavegameDatDecompressionFailedException(
                            $"Payload length {payload.Length} is smaller than expected size {envelope.ExpectedDecompressedSize}.");
                    }

                    // STFS block alignment may produce trailing padding; truncate to expected length.
                    byte[] decoded = new byte[envelope.ExpectedDecompressedSize];
                    Array.Copy(payload, decoded, envelope.ExpectedDecompressedSize);
                    return decoded;
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
                "XMemLzxOnly",
                (envelope, payload) => _xmem.Value.Decompress(payload, envelope.ExpectedDecompressedSize)),
            new DecoderCandidate(
                "XMemLzxThenRle",
                (envelope, payload) => _xmem.Value.DecompressLzxRle(payload, envelope.ExpectedDecompressedSize)),
            new DecoderCandidate(
                "XMemLzxThenRleShift4",
                (envelope, payload) =>
                {
                    if (payload.Length <= 4)
                        throw new SavegameDatDecompressionFailedException("Payload too small for 4-byte shift.");
                    return _xmem.Value.DecompressLzxRle(payload[4..], envelope.ExpectedDecompressedSize);
                }),
            new DecoderCandidate(
                "InnerSizeBeStored",
                static (envelope, payload) => DecodeInnerSizePrefixedStored(payload)),
            new DecoderCandidate(
                "InnerSizeBeRle",
                static (envelope, payload) => DecodeInnerSizePrefixedRle(payload)),
            new DecoderCandidate(
                "InnerSizeBeXMemLzx",
                (envelope, payload) => DecodeInnerSizePrefixedXMem(payload, rleDecode: false)),
            new DecoderCandidate(
                "InnerSizeBeXMemLzxThenRle",
                (envelope, payload) => DecodeInnerSizePrefixedXMem(payload, rleDecode: true)),
        ];
    }

    private static byte[] DecodeInnerSizePrefixedStored(byte[] payload)
    {
        int expectedSize = ReadInnerBigEndianSize(payload);
        ReadOnlySpan<byte> storedBytes = payload.AsSpan(4);
        if (storedBytes.Length < expectedSize)
        {
            throw new SavegameDatDecompressionFailedException(
                $"Inner payload length {storedBytes.Length} is smaller than expected size {expectedSize}.");
        }

        byte[] decoded = new byte[expectedSize];
        storedBytes[..expectedSize].CopyTo(decoded);
        return decoded;
    }

    private static byte[] DecodeInnerSizePrefixedRle(byte[] payload)
    {
        int expectedSize = ReadInnerBigEndianSize(payload);
        return SavegameRleCodec.Decode(payload.AsSpan(4), expectedSize);
    }

    private byte[] DecodeInnerSizePrefixedXMem(byte[] payload, bool rleDecode)
    {
        int expectedSize = ReadInnerBigEndianSize(payload);

        ReadOnlySpan<byte> compressedBytes = payload.AsSpan(4);
        if (!rleDecode)
        {
            return XboxXMemDecompressService.DecompressWithKnownContextVariants(compressedBytes, expectedSize);
        }

        int maxRleBytes = checked(Math.Max(expectedSize + 64 * 1024, expectedSize * 2));
        byte[] rleBytes = XboxXMemDecompressService.DecompressUpToWithKnownContextVariants(compressedBytes, maxRleBytes);
        return SavegameRleCodec.Decode(rleBytes, expectedSize);
    }

    private static int ReadInnerBigEndianSize(byte[] payload)
    {
        if (payload.Length <= 4)
        {
            throw new SavegameDatDecompressionFailedException("Payload is too small for an inner size prefix.");
        }

        int expectedSize = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
        if (expectedSize <= 0 || expectedSize > 256 * 1024 * 1024)
        {
            throw new SavegameDatDecompressionFailedException(
                $"Inner big-endian size prefix is not plausible: {expectedSize}.");
        }

        return expectedSize;
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

    public void Dispose()
    {
        if (_xmem.IsValueCreated)
            _xmem.Value.Dispose();
    }
}
