namespace Console2Lce;

public static class SavegameInputReader
{
    public static SavegameInputData Read(ReadOnlyMemory<byte> inputBytes)
    {
        if (LooksLikeStfsPackage(inputBytes.Span))
        {
            StfsPackageMetadata metadata = StfsPackageDescriptorReader.Read(inputBytes.Span);
            var reader = new StfsReader();
            IReadOnlyList<StfsFileEntry> entries = reader.EnumerateEntries(inputBytes);
            StfsExtractedFile savegame = reader.ReadFileWithContext(inputBytes, "savegame.dat");
            byte[] leadingPrefixBytes = savegame.FirstBlockOffset >= 4
                ? inputBytes.Span.Slice(savegame.FirstBlockOffset - 4, 4).ToArray()
                : Array.Empty<byte>();

            return new SavegameInputData(
                true,
                metadata,
                entries,
                savegame.Bytes,
                savegame.FirstBlockOffset,
                leadingPrefixBytes);
        }

        return new SavegameInputData(
            false,
            null,
            Array.Empty<StfsFileEntry>(),
            inputBytes.ToArray(),
            null,
            Array.Empty<byte>());
    }

    private static bool LooksLikeStfsPackage(ReadOnlySpan<byte> inputBytes)
    {
        try
        {
            _ = StfsPackageMagicReader.ReadPackageType(inputBytes);
            return true;
        }
        catch (InvalidXboxPackageMagicException)
        {
            return false;
        }
    }
}
