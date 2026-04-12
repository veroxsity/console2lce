using System.Buffers.Binary;

namespace Console2Lce;

public static class SavegameEnvelopeReader
{
    public static IReadOnlyList<SavegameEnvelopeCandidate> ReadCandidates(ReadOnlySpan<byte> savegameBytes)
    {
        if (savegameBytes.Length < 8)
        {
            return Array.Empty<SavegameEnvelopeCandidate>();
        }

        int payloadLength = savegameBytes.Length - 8;
        int leadingPayloadLength = savegameBytes.Length - 4;

        return
        [
            CreateCandidate(
                name: "HeaderAt0LittleEndian",
                description: "Interpret the first 8 bytes as the normal save envelope using little-endian integers.",
                endianness: "little",
                compressionFlag: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes[..4]),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes.Slice(4, 4)),
                payloadOffset: 8,
                payloadLength: payloadLength),
            CreateCandidate(
                name: "HeaderAt0BigEndian",
                description: "Interpret the first 8 bytes as the normal save envelope using big-endian integers.",
                endianness: "big",
                compressionFlag: BinaryPrimitives.ReadInt32BigEndian(savegameBytes[..4]),
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(savegameBytes.Slice(4, 4)),
                payloadOffset: 8,
                payloadLength: payloadLength),
            CreateCandidate(
                name: "SyntheticMissingZeroFlagLittleEndian",
                description: "Heuristic: assume the extracted file is missing a leading zero compression flag and the current first 4 bytes are the decompressed size in little-endian form.",
                endianness: "little",
                compressionFlag: 0,
                expectedDecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(savegameBytes[..4]),
                payloadOffset: 4,
                payloadLength: leadingPayloadLength),
            CreateCandidate(
                name: "SyntheticMissingZeroFlagBigEndian",
                description: "Heuristic: assume the extracted file is missing a leading zero compression flag and the current first 4 bytes are the decompressed size in big-endian form.",
                endianness: "big",
                compressionFlag: 0,
                expectedDecompressedSize: BinaryPrimitives.ReadInt32BigEndian(savegameBytes[..4]),
                payloadOffset: 4,
                payloadLength: leadingPayloadLength),
        ];
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
