using System.Buffers.Binary;
using System.IO.Compression;

namespace Console2Lce;

public sealed class SavegameProbeService
{
    private const int ArchiveHeaderSize = 8;

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

            byte[] payload = savegameBytes.Span[envelope.PayloadOffset..].ToArray();
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
            findings.Add("Xbox 360 saves likely use the native LZXRLE/XMem path, which is not implemented yet.");
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
        ];
    }

    private static byte[] ZlibDecompress(byte[] payload)
    {
        using var input = new MemoryStream(payload, writable: false);
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static bool TryReadPlausibleArchiveHeader(
        ReadOnlySpan<byte> bytes,
        out int indexOffset,
        out int fileCount)
    {
        indexOffset = 0;
        fileCount = 0;

        if (bytes.Length < ArchiveHeaderSize)
        {
            return false;
        }

        indexOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes[..4]);
        fileCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4));

        if (indexOffset < ArchiveHeaderSize || indexOffset > bytes.Length)
        {
            return false;
        }

        if (fileCount <= 0 || fileCount > 1_000_000)
        {
            return false;
        }

        return true;
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
                bool plausibleArchive = TryReadPlausibleArchiveHeader(decoded, out int indexOffset, out int fileCount);

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
                        "Decoded bytes did not produce a plausible little-endian archive header.");
                }

                return new SavegameProbeAttempt(
                    envelope.Name,
                    Name,
                    true,
                    decoded.Length,
                    ToHexPreview(decoded, 16),
                    true,
                    indexOffset,
                    fileCount,
                    null);
            }
            catch (Exception exception) when (exception is InvalidDataException or SavegameDatDecompressionFailedException)
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
