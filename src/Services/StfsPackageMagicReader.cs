using System.Buffers.Binary;

namespace Console2Lce;

public static class StfsPackageMagicReader
{
    private const uint ConMagic = 0x434F4E20;
    private const uint LiveMagic = 0x4C495645;
    private const uint PirsMagic = 0x50495253;

    public static StfsPackageType ReadPackageType(ReadOnlySpan<byte> packageBytes)
    {
        if (packageBytes.Length < sizeof(uint))
        {
            throw new InvalidXboxPackageMagicException();
        }

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(packageBytes);
        return magic switch
        {
            ConMagic => StfsPackageType.Con,
            LiveMagic => StfsPackageType.Live,
            PirsMagic => StfsPackageType.Pirs,
            _ => throw new InvalidXboxPackageMagicException(),
        };
    }
}
