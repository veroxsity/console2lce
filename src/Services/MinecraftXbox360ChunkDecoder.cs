namespace Console2Lce;

public sealed class MinecraftXbox360ChunkDecoder
{
    private static ChunkDecoderCandidate? _preferredCandidate;
    private readonly IMinecraftXbox360ChunkExternalDecoder? _externalDecoder;

    public MinecraftXbox360ChunkDecoder()
        : this(new MccXboxSupportChunkExternalDecoder())
    {
    }

    public MinecraftXbox360ChunkDecoder(IMinecraftXbox360ChunkExternalDecoder? externalDecoder)
    {
        _externalDecoder = externalDecoder;
    }

    public MinecraftXbox360ChunkDecodeReport DecodeSample(
        string regionFileName,
        MinecraftXbox360RegionChunk chunk,
        ReadOnlyMemory<byte> regionBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionFileName);

        byte[] compressedBytes = regionBytes.Slice(chunk.PayloadOffset, chunk.StoredLength).ToArray();
        var attempts = new List<MinecraftXbox360ChunkDecodeAttempt>();

        foreach (ChunkDecoderCandidate candidate in EnumerateCandidates())
        {
            try
            {
                byte[] decoded = candidate.Decode(compressedBytes, chunk.DecompressedLength, chunk.UsesRleCompression);
                if (!MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(decoded, out string payloadKind, out int? chunkX, out int? chunkZ, out bool? hasLevelWrapper))
                {
                    attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                        candidate.Name,
                        false,
                        decoded.Length,
                        null,
                        null,
                        null,
                        null,
                        "Decoded bytes did not match a known console chunk payload shape."));
                    continue;
                }

                _preferredCandidate ??= candidate;
                attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                    candidate.Name,
                    true,
                    decoded.Length,
                    payloadKind,
                    chunkX,
                    chunkZ,
                    hasLevelWrapper,
                    null));

                return new MinecraftXbox360ChunkDecodeReport(
                    regionFileName,
                    chunk.Index,
                    chunk.X,
                    chunk.Z,
                    true,
                    candidate.Name,
                    decoded.Length,
                    payloadKind,
                    chunkX,
                    chunkZ,
                    hasLevelWrapper,
                    attempts);
            }
            catch (Exception exception)
            {
                attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                    candidate.Name,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    exception.Message));
            }
        }

        if (_externalDecoder is not null)
        {
            if (_externalDecoder.TryDecode(compressedBytes, chunk.DecompressedLength, out byte[] decoded, out string? failure))
            {
                if (!MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(decoded, out string payloadKind, out int? chunkX, out int? chunkZ, out bool? hasLevelWrapper))
                {
                    attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                        _externalDecoder.DecoderName,
                        false,
                        decoded.Length,
                        null,
                        null,
                        null,
                        null,
                        "Decoded bytes did not match a known console chunk payload shape."));
                }
                else
                {
                    attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                        _externalDecoder.DecoderName,
                        true,
                        decoded.Length,
                        payloadKind,
                        chunkX,
                        chunkZ,
                        hasLevelWrapper,
                        null));

                    return new MinecraftXbox360ChunkDecodeReport(
                        regionFileName,
                        chunk.Index,
                        chunk.X,
                        chunk.Z,
                        true,
                        _externalDecoder.DecoderName,
                        decoded.Length,
                        payloadKind,
                        chunkX,
                        chunkZ,
                        hasLevelWrapper,
                        attempts);
                }
            }
            else if (!string.IsNullOrWhiteSpace(failure))
            {
                attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                    _externalDecoder.DecoderName,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    failure));
            }
        }

        return new MinecraftXbox360ChunkDecodeReport(
            regionFileName,
            chunk.Index,
            chunk.X,
            chunk.Z,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            attempts);
    }

    private static IEnumerable<ChunkDecoderCandidate> EnumerateCandidates()
    {
        if (_preferredCandidate is not null)
        {
            yield return _preferredCandidate;
        }

        foreach (ChunkDecoderCandidate candidate in ChunkDecoderCandidate.All)
        {
            if (!ReferenceEquals(candidate, _preferredCandidate))
            {
                yield return candidate;
            }
        }
    }

    private sealed class ChunkDecoderCandidate
    {
        public static readonly ChunkDecoderCandidate[] All =
        [
            new("XboxLzxThenRle_128k_128k", 128 * 1024, 128 * 1024),
            new("XboxLzxThenRle_128k_512k", 128 * 1024, 512 * 1024),
            new("XboxLzxThenRle_64k_128k", 64 * 1024, 128 * 1024),
            new("XboxLzxThenRle_32k_32k", 32 * 1024, 32 * 1024),
            new("XboxLzxThenRle_256k_256k", 256 * 1024, 256 * 1024),
        ];

        public ChunkDecoderCandidate(string name, int windowSize, int partitionSize)
        {
            Name = name;
            WindowSize = windowSize;
            PartitionSize = partitionSize;
        }

        public string Name { get; }

        public int WindowSize { get; }

        public int PartitionSize { get; }

        public byte[] Decode(byte[] compressedBytes, int expectedDecompressedSize, bool usesRleCompression)
        {
            if (!usesRleCompression)
            {
                return XboxLzxNativeDecoder.Decompress(compressedBytes, expectedDecompressedSize, WindowSize, PartitionSize);
            }

            int intermediateBufferSize = XboxLzxNativeDecoder.GetRecommendedIntermediateBufferSize(expectedDecompressedSize);
            byte[] rleBytes = XboxLzxNativeDecoder.Decompress(compressedBytes, intermediateBufferSize, WindowSize, PartitionSize);
            return SavegameRleCodec.Decode(rleBytes, expectedDecompressedSize);
        }
    }
}
