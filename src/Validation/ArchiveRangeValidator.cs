namespace Console2Lce;

public static class ArchiveRangeValidator
{
    public static void EnsureWithinBounds(int offset, int length, int totalLength, string? entryName = null)
    {
        if (totalLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLength), "Total length cannot be negative.");
        }

        if (offset < 0)
        {
            throw CreateException("Offset cannot be negative.", entryName);
        }

        if (length < 0)
        {
            throw CreateException("Length cannot be negative.", entryName);
        }

        long endOffset = (long)offset + length;
        if (endOffset > totalLength)
        {
            throw CreateException(
                $"Range [{offset}, {endOffset}) exceeds payload length {totalLength}.",
                entryName);
        }
    }

    private static ArchiveEntryOutOfBoundsException CreateException(string message, string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return new ArchiveEntryOutOfBoundsException(message);
        }

        return new ArchiveEntryOutOfBoundsException($"Entry '{entryName}' is out of bounds: {message}");
    }
}
