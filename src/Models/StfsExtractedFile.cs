namespace Console2Lce;

public sealed record StfsExtractedFile(
    string Name,
    int Size,
    int StartingBlock,
    int FirstBlockOffset,
    byte[] Bytes);
