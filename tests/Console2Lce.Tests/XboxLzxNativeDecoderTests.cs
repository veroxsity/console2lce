namespace Console2Lce.Tests;

public sealed class XboxLzxNativeDecoderTests
{
    [Fact]
    public void GetRecommendedIntermediateBufferSize_UsesExpectedSizeWhenLargerThanStaticFloor()
    {
        int bufferSize = XboxLzxNativeDecoder.GetRecommendedIntermediateBufferSize(600_000);

        Assert.Equal(600_000, bufferSize);
    }

    [Fact]
    public void GetRecommendedIntermediateBufferSize_UsesStaticFloorForSmallOutputs()
    {
        int bufferSize = XboxLzxNativeDecoder.GetRecommendedIntermediateBufferSize(32_768);

        Assert.Equal(200 * 1024, bufferSize);
    }

    [Fact]
    public void GetRecommendedIntermediateBufferSize_RejectsOversizedBuffers()
    {
        var exception = Assert.Throws<SavegameDatDecompressionFailedException>(
            () => XboxLzxNativeDecoder.GetRecommendedIntermediateBufferSize((16 * 1024 * 1024) + 1));

        Assert.Contains("intermediate buffer limit", exception.Message, StringComparison.Ordinal);
    }
}
