namespace Console2Lce.Tests;

public sealed class ArchiveRangeValidatorTests
{
    [Fact]
    public void EnsureWithinBounds_AllowsValidRange()
    {
        ArchiveRangeValidator.EnsureWithinBounds(offset: 12, length: 20, totalLength: 64, entryName: "level.dat");
    }

    [Fact]
    public void EnsureWithinBounds_ThrowsForNegativeOffset()
    {
        ArchiveEntryOutOfBoundsException exception = Assert.Throws<ArchiveEntryOutOfBoundsException>(
            () => ArchiveRangeValidator.EnsureWithinBounds(offset: -1, length: 20, totalLength: 64, entryName: "level.dat"));

        Assert.Contains("level.dat", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureWithinBounds_ThrowsWhenRangeExceedsPayload()
    {
        ArchiveEntryOutOfBoundsException exception = Assert.Throws<ArchiveEntryOutOfBoundsException>(
            () => ArchiveRangeValidator.EnsureWithinBounds(offset: 60, length: 8, totalLength: 64, entryName: "players/0.dat"));

        Assert.Contains("players/0.dat", exception.Message, StringComparison.Ordinal);
    }
}
