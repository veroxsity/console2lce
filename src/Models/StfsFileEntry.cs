namespace Console2Lce;

public sealed record StfsFileEntry(
    string Name,
    int Size,
    int? StartingBlock = null);
