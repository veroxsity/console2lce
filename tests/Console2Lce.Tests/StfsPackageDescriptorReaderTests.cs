namespace Console2Lce.Tests;

public sealed class StfsPackageDescriptorReaderTests
{
    [Fact]
    public void Read_ParsesCoreMetadataFields()
    {
        byte[] bytes = new byte[0x3A0];
        bytes[0] = (byte)'C';
        bytes[1] = (byte)'O';
        bytes[2] = (byte)'N';
        bytes[3] = (byte)' ';

        bytes[0x340] = 0x00;
        bytes[0x341] = 0x00;
        bytes[0x342] = 0x97;
        bytes[0x343] = 0x1A;

        bytes[0x379] = 0x24;
        bytes[0x37B] = 0x02;
        bytes[0x37C] = 0x01;
        bytes[0x37D] = 0x00;
        bytes[0x37E] = 0x12;
        bytes[0x37F] = 0x02;
        bytes[0x380] = 0x00;

        StfsPackageMetadata metadata = StfsPackageDescriptorReader.Read(bytes);

        Assert.Equal(StfsPackageType.Con, metadata.PackageType);
        Assert.Equal(0x971A, metadata.HeaderSize);
        Assert.Equal(0xA000, metadata.HeaderAlignedSize);
        Assert.Equal(2, metadata.BlockSeparation);
        Assert.Equal(1, metadata.FileTableBlockCount);
        Assert.Equal(0x212, metadata.FileTableBlockNumber);
    }
}
