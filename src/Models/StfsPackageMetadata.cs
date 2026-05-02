namespace Console2Lce;

public sealed record StfsPackageMetadata(
    StfsPackageType PackageType,
    int HeaderSize,
    int HeaderAlignedSize,
    int BlockSeparation,
    int FileTableBlockCount,
    int FileTableBlockNumber,
    int TopRecordIndex);
