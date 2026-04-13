namespace Console2Lce.Tests;

public sealed class WindowsCompressionApiTests
{
    [Fact]
    public void CompressAndDecompress_RoundTripsXpressRaw()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        byte[] input = System.Text.Encoding.ASCII.GetBytes(new string('A', 2048) + new string('B', 1024));

        byte[] compressed = WindowsCompressionApi.Compress(
            input,
            WindowsCompressionApi.CompressAlgorithmXpress | WindowsCompressionApi.CompressRaw);
        byte[] decompressed = WindowsCompressionApi.Decompress(
            compressed,
            input.Length,
            WindowsCompressionApi.CompressAlgorithmXpress | WindowsCompressionApi.CompressRaw);

        Assert.Equal(input, decompressed);
    }
}
