namespace Console2Lce;

public interface IMinecraft360ArchiveParser
{
    Minecraft360Archive Parse(ReadOnlyMemory<byte> decompressedBytes);
}
