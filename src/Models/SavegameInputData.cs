namespace Console2Lce;

public sealed record SavegameInputData(
    bool IsStfsPackage,
    StfsPackageMetadata? Metadata,
    IReadOnlyList<StfsFileEntry> Entries,
    byte[] SavegameBytes,
    int? FirstBlockOffset,
    byte[] LeadingPrefixBytes);
