using System.Buffers.Binary;

namespace Console2Lce;

public static class SavegameEnvelopeReader
{
    public static IReadOnlyList<SavegameEnvelopeCandidate> ReadCandidates(
        ReadOnlySpan<byte> savegameBytes,
        ReadOnlySpan<byte> leadingPrefixBytes = default)
    {
        if (savegameBytes.Length < 8 && (leadingPrefixBytes.Length != 4 || savegameBytes.Length < 4))
        {
            return Array.Empty<SavegameEnvelopeCandidate>();
        }

        var candidates = new List<SavegameEnvelopeCandidate>();
        if (savegameBytes.Length >= 8)
        {
            int payloadLength = savegameBytes.Length - 8;

            candidates.Add(CreateCandidate(
                name: "HeaderAt0LittleEndian",
                description: "Interpret the first 8 bytes as the normal save envelope using little-endian integers.",
                endianness: "little",
                compressionFlag: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes[..4]),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes.Slice(4, 4)),
                payloadOffset: 8,
                payloadLength: payloadLength));
            candidates.Add(CreateCandidate(
                name: "HeaderAt0BigEndian",
                description: "Interpret the first 8 bytes as the normal save envelope using big-endian integers.",
                endianness: "big",
                compressionFlag: BinaryPrimitives.ReadInt32BigEndian(savegameBytes[..4]),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(savegameBytes.Slice(4, 4)),
                payloadOffset: 8,
                payloadLength: payloadLength));
        }

        if (leadingPrefixBytes.Length == 4 && savegameBytes.Length >= 4)
        {
            byte[] reconstructedHeader = new byte[8];
            leadingPrefixBytes.CopyTo(reconstructedHeader.AsSpan(0, 4));
            savegameBytes[..4].CopyTo(reconstructedHeader.AsSpan(4, 4));

            candidates.Add(CreateCandidate(
                name: "RecoveredPrefixHeaderLittleEndian",
                description: "Heuristic: reconstruct the 8-byte save envelope by prepending the 4 bytes that immediately precede the computed first file block.",
                endianness: "little",
                compressionFlag: BinaryPrimitives.ReadInt32LittleEndian(reconstructedHeader.AsSpan(0, 4)),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(reconstructedHeader.AsSpan(4, 4)),
                payloadOffset: 4,
                payloadLength: savegameBytes.Length - 4));
            candidates.Add(CreateCandidate(
                name: "RecoveredPrefixHeaderBigEndian",
                description: "Heuristic: reconstruct the 8-byte save envelope by prepending the 4 bytes that immediately precede the computed first file block.",
                endianness: "big",
                compressionFlag: BinaryPrimitives.ReadInt32BigEndian(reconstructedHeader.AsSpan(0, 4)),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(reconstructedHeader.AsSpan(4, 4)),
                payloadOffset: 4,
                payloadLength: savegameBytes.Length - 4));
            candidates.Add(CreateCandidate(
                name: "RecoveredPrefixShiftedWindowLittleEndian",
                description: "Heuristic: reconstruct the missing 4-byte prefix and assume the extracted byte window also includes 4 trailing bytes from after the file.",
                endianness: "little",
                compressionFlag: BinaryPrimitives.ReadInt32LittleEndian(reconstructedHeader.AsSpan(0, 4)),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(reconstructedHeader.AsSpan(4, 4)),
                payloadOffset: 4,
                payloadLength: savegameBytes.Length - 8));
            candidates.Add(CreateCandidate(
                name: "RecoveredPrefixShiftedWindowBigEndian",
                description: "Heuristic: reconstruct the missing 4-byte prefix and assume the extracted byte window also includes 4 trailing bytes from after the file.",
                endianness: "big",
                compressionFlag: BinaryPrimitives.ReadInt32BigEndian(reconstructedHeader.AsSpan(0, 4)),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(reconstructedHeader.AsSpan(4, 4)),
                payloadOffset: 4,
                payloadLength: savegameBytes.Length - 8));
        }

        if (savegameBytes.Length >= 4)
        {
            int leadingPayloadLength = savegameBytes.Length - 4;

            candidates.Add(CreateCandidate(
                name: "SyntheticMissingZeroFlagLittleEndian",
                description: "Heuristic: assume the extracted file is missing a leading zero compression flag and the current first 4 bytes are the decompressed size in little-endian form.",
                endianness: "little",
                compressionFlag: 0,
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes[..4]),
                payloadOffset: 4,
                payloadLength: leadingPayloadLength));
            candidates.Add(CreateCandidate(
                name: "SyntheticMissingZeroFlagBigEndian",
                description: "Heuristic: assume the extracted file is missing a leading zero compression flag and the current first 4 bytes are the decompressed size in big-endian form.",
                endianness: "big",
                compressionFlag: 0,
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(savegameBytes[..4]),
                payloadOffset: 4,
                payloadLength: leadingPayloadLength));
            candidates.Add(CreateCandidate(
                name: "SyntheticMissingZeroFlagShiftedWindowLittleEndian",
                description: "Heuristic: assume the extracted file is missing a leading zero compression flag and also includes 4 trailing bytes from after the real file window.",
                endianness: "little",
                compressionFlag: 0,
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes[..4]),
                payloadOffset: 4,
                payloadLength: savegameBytes.Length - 8));
            candidates.Add(CreateCandidate(
                name: "SyntheticMissingZeroFlagShiftedWindowBigEndian",
                description: "Heuristic: assume the extracted file is missing a leading zero compression flag and also includes 4 trailing bytes from after the real file window.",
                endianness: "big",
                compressionFlag: 0,
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(savegameBytes[..4]),
                payloadOffset: 4,
                payloadLength: savegameBytes.Length - 8));
        }

        return candidates;
    }

    private static SavegameEnvelopeCandidate CreateCandidate(
        string name,
        string description,
        string endianness,
        int compressionFlag,
        int expectedDecompressedSize,
        int payloadOffset,
        int payloadLength)
    {
        bool isPlausible = compressionFlag == 0
            && expectedDecompressedSize > 0
            && expectedDecompressedSize <= 256 * 1024 * 1024
            && payloadOffset >= 0
            && payloadLength >= 0;

        return new SavegameEnvelopeCandidate(
            name,
            description,
            endianness,
            compressionFlag,
            expectedDecompressedSize,
            payloadOffset,
            payloadLength,
            isPlausible);
    }
}
