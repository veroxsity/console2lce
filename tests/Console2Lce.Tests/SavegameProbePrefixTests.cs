using System.Buffers.Binary;

namespace Console2Lce.Tests;

public sealed class SavegameProbePrefixTests
{
    [Fact]
    public void Probe_UsesRecoveredPrefixToRecognizeACompleteEnvelope()
    {
        byte[] archive = new byte[0x30];
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(0, 4), 0x20);
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(4, 4), 2);

        byte[] fullSavegame = new byte[archive.Length + 8];
        BinaryPrimitives.WriteInt32BigEndian(fullSavegame.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(fullSavegame.AsSpan(4, 4), archive.Length);
        archive.CopyTo(fullSavegame.AsSpan(8));

        byte[] extractedBytes = fullSavegame[4..];
        byte[] recoveredPrefix = fullSavegame[..4];

        SavegameProbeResult result = new SavegameProbeService().Probe(extractedBytes, recoveredPrefix);

        Assert.True(result.Report.HasSuccessfulDecompression);
        Assert.Equal("RecoveredPrefixHeaderBigEndian", result.Report.RecommendedEnvelope);
        Assert.Equal("StoredWithoutCompression", result.Report.RecommendedDecoder);
        Assert.Equal(archive, result.DecompressedBytes);
    }
}
