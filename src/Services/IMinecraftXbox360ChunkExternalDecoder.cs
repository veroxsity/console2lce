namespace Console2Lce;

public interface IMinecraftXbox360ChunkExternalDecoder
{
    string DecoderName { get; }

    bool TryDecode(ReadOnlySpan<byte> compressedBytes, int expectedDecompressedSize, out byte[] decodedBytes, out string? failure);
}
