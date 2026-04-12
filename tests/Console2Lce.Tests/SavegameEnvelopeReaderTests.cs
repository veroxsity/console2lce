using System.Buffers.Binary;

namespace Console2Lce.Tests;

public sealed class SavegameEnvelopeReaderTests
{
    [Fact]
    public void ReadCandidates_IncludesNormalAndSyntheticInterpretations()
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 0x1234);

        IReadOnlyList<SavegameEnvelopeCandidate> candidates = SavegameEnvelopeReader.ReadCandidates(bytes);

        Assert.Equal(4, candidates.Count);
        Assert.Equal("HeaderAt0LittleEndian", candidates[0].Name);
        Assert.Equal(0x1234, candidates[0].ExpectedDecompressedSize);
        Assert.True(candidates[0].IsPlausible);
        Assert.Equal("SyntheticMissingZeroFlagBigEndian", candidates[3].Name);
        Assert.Equal(4, candidates[3].PayloadOffset);
    }
}
