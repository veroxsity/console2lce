namespace Console2Lce.Tests;

public sealed class StfsPackageMagicReaderTests
{
    [Theory]
    [InlineData("CON ", StfsPackageType.Con)]
    [InlineData("LIVE", StfsPackageType.Live)]
    [InlineData("PIRS", StfsPackageType.Pirs)]
    public void ReadPackageType_ReturnsExpectedPackageType(string magic, StfsPackageType expected)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(magic);

        StfsPackageType actual = StfsPackageMagicReader.ReadPackageType(bytes);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadPackageType_ThrowsForUnknownMagic()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("ABCD");

        Assert.Throws<InvalidXboxPackageMagicException>(() => StfsPackageMagicReader.ReadPackageType(bytes));
    }
}
