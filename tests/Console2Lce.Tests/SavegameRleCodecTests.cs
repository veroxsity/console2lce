using System.Text;

namespace Console2Lce.Tests;

public sealed class SavegameRleCodecTests
{
    [Fact]
    public void Decode_DecodesLiteralRunsAndRepeatedBytes()
    {
        byte[] encoded =
        [
            (byte)'A',
            0xFF, 0x00,
            0xFF, 0x03, (byte)'B',
            (byte)'C',
        ];

        byte[] decoded = SavegameRleCodec.Decode(encoded, 7);

        Assert.Equal("A" + "\u00FF" + "BBBB" + "C", Encoding.Latin1.GetString(decoded));
    }

    [Fact]
    public void Decode_ThrowsWhenOutputLengthDoesNotMatch()
    {
        byte[] encoded = [(byte)'A'];

        SavegameDatDecompressionFailedException exception = Assert.Throws<SavegameDatDecompressionFailedException>(
            () => SavegameRleCodec.Decode(encoded, 2));

        Assert.Contains("expected 2", exception.Message);
    }
}
