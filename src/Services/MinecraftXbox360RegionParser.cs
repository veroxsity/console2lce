using System.Buffers.Binary;

namespace Console2Lce;

public sealed class MinecraftXbox360RegionParser
{
    public const int SectorBytes = 4096;
    public const int SectorInts = SectorBytes / 4;
    public const int ChunkHeaderSize = 8;
    public const int HeaderSectors = 2;
    public const int HeaderBytes = HeaderSectors * SectorBytes;

    public MinecraftXbox360Region Parse(ReadOnlyMemory<byte> regionBytes, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ReadOnlySpan<byte> bytes = regionBytes.Span;

        if (bytes.Length < HeaderBytes)
        {
            throw new InvalidMinecraftXbox360RegionException(
                $"Region '{fileName}' is too small to contain the 8 KiB header.");
        }

        int paddedLength = checked(((bytes.Length + SectorBytes - 1) / SectorBytes) * SectorBytes);
        var chunks = new List<MinecraftXbox360RegionChunk>();
        for (int index = 0; index < SectorInts; index++)
        {
            int tableOffset = index * sizeof(int);
            int rawOffset = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(tableOffset, sizeof(int)));
            int timestamp = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(SectorBytes + tableOffset, sizeof(int)));

            if (rawOffset == 0)
            {
                continue;
            }

            int sectorNumber = rawOffset >> 8;
            int sectorCount = rawOffset & 0xFF;

            if (sectorNumber < HeaderSectors)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} points into the header sectors.");
            }

            if (sectorCount <= 0)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} has an invalid sector count of {sectorCount}.");
            }

            int chunkOffset = checked(sectorNumber * SectorBytes);
            int chunkAllocationBytes = checked(sectorCount * SectorBytes);
            int chunkEndOffset = checked(chunkOffset + chunkAllocationBytes);

            // Region entries are stored in sector units, but the archive may trim trailing
            // sector padding. The game pads region files back to 4 KiB on open.
            if (chunkEndOffset > paddedLength)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} extends beyond the region length.");
            }

            if (chunkOffset + ChunkHeaderSize > bytes.Length)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} is missing its chunk header.");
            }

            ReadOnlySpan<byte> chunkHeader = bytes.Slice(chunkOffset, ChunkHeaderSize);
            uint storedLengthWithFlags = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..sizeof(uint)]);
            int decompressedLength = BinaryPrimitives.ReadInt32BigEndian(chunkHeader.Slice(sizeof(uint), sizeof(int)));
            bool usesRleCompression = (storedLengthWithFlags & 0x80000000u) != 0;
            int storedLength = checked((int)(storedLengthWithFlags & 0x7FFFFFFFu));
            int payloadOffset = checked(chunkOffset + ChunkHeaderSize);
            int payloadEndOffset = checked(payloadOffset + storedLength);

            if (decompressedLength <= 0)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} has an invalid decompressed length of {decompressedLength}.");
            }

            if (storedLength <= 0)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} has an invalid stored length of {storedLength}.");
            }

            if (payloadEndOffset > chunkEndOffset)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} overruns its allocated sectors.");
            }

            if (payloadEndOffset > bytes.Length)
            {
                throw new InvalidMinecraftXbox360RegionException(
                    $"Region '{fileName}' chunk slot {index} is missing payload bytes.");
            }

            int x = index % 32;
            int z = index / 32;
            chunks.Add(new MinecraftXbox360RegionChunk(
                index,
                x,
                z,
                timestamp,
                sectorNumber,
                sectorCount,
                chunkOffset,
                payloadOffset,
                storedLength,
                decompressedLength,
                usesRleCompression));
        }

        return new MinecraftXbox360Region(fileName, bytes.Length, chunks);
    }
}
