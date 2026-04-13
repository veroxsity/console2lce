using System.Text.RegularExpressions;

namespace Console2Lce;

public sealed class MinecraftXbox360ChunkDecoder
{
    private static readonly object FailedPayloadDumpLock = new();
    private static bool _failedPayloadDumpWritten;

    private static readonly Regex RegionNamePattern = new(
        @"r\.(?<x>-?\d+)\.(?<z>-?\d+)\.mcr$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        if (!TryDecodeChunkPayload(compressedBytes, chunk.DecompressedLength, chunk.UsesRleCompression, out byte[] decoded, out string? decoder, out string? failure, attempts))
        {
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

        if (!MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(decoded, out string payloadKind, out int? chunkX, out int? chunkZ, out bool? hasLevelWrapper))
        {
            attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
                decoder ?? "Unknown",
                false,
                decoded.Length,
                null,
                null,
                null,
                null,
                "Decoded bytes did not match a known console chunk payload shape."));

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

        _preferredCandidate ??= ChunkDecoderCandidate.All.FirstOrDefault(candidate => candidate.Name == decoder);
        (chunkX, chunkZ) = ResolveChunkCoordinates(regionFileName, chunk, chunkX, chunkZ);
        attempts.Add(new MinecraftXbox360ChunkDecodeAttempt(
            decoder!,
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
            decoder,
            decoded.Length,
            payloadKind,
            chunkX,
            chunkZ,
            hasLevelWrapper,
            attempts);
    }

    public bool TryDecodeChunkPayload(
        byte[] compressedBytes,
        int expectedDecompressedSize,
        bool usesRleCompression,
        out byte[] decodedBytes,
        out string? decoderName,
        out string? failure,
        List<MinecraftXbox360ChunkDecodeAttempt>? attempts = null)
    {
        ArgumentNullException.ThrowIfNull(compressedBytes);

        foreach (ChunkDecoderCandidate candidate in EnumerateCandidates())
        {
            try
            {
                byte[] decoded = candidate.Decode(compressedBytes, expectedDecompressedSize, usesRleCompression);
                if (!MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(decoded, out _, out _, out _, out _))
                {
                    TryDumpFailedPayload(decoded, candidate.Name);
                    attempts?.Add(new MinecraftXbox360ChunkDecodeAttempt(
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

                decodedBytes = decoded;
                decoderName = candidate.Name;
                failure = null;
                _preferredCandidate ??= candidate;
                return true;
            }
            catch (Exception exception)
            {
                attempts?.Add(new MinecraftXbox360ChunkDecodeAttempt(
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
            if (_externalDecoder.TryDecode(compressedBytes, expectedDecompressedSize, out byte[] decoded, out string? externalFailure))
            {
                if (!MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(decoded, out _, out _, out _, out _))
                {
                    TryDumpFailedPayload(decoded, _externalDecoder.DecoderName);
                    attempts?.Add(new MinecraftXbox360ChunkDecodeAttempt(
                        _externalDecoder.DecoderName,
                        false,
                        decoded.Length,
                        null,
                        null,
                        null,
                        null,
                        "Decoded bytes did not match a known console chunk payload shape."));

                    decodedBytes = Array.Empty<byte>();
                    decoderName = null;
                    failure = "No Xbox 360 chunk decoder produced a recognized payload.";
                    return false;
                }

                decodedBytes = decoded;
                decoderName = _externalDecoder.DecoderName;
                failure = null;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(externalFailure))
            {
                attempts?.Add(new MinecraftXbox360ChunkDecodeAttempt(
                    _externalDecoder.DecoderName,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    externalFailure));
            }
        }

        decodedBytes = Array.Empty<byte>();
        decoderName = null;
        failure = "No Xbox 360 chunk decoder succeeded.";
        return false;
    }

    private static void TryDumpFailedPayload(byte[] decoded, string decoderName)
    {
        string? dumpPath = Environment.GetEnvironmentVariable("CONSOLE2LCE_DUMP_FAILED_CHUNK");
        if (string.IsNullOrWhiteSpace(dumpPath))
        {
            return;
        }

        lock (FailedPayloadDumpLock)
        {
            if (_failedPayloadDumpWritten)
            {
                return;
            }

            string fullPath = Path.GetFullPath(dumpPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            File.WriteAllBytes(fullPath, decoded);
            File.WriteAllText(
                fullPath + ".txt",
                $"Decoder={decoderName}{Environment.NewLine}Length={decoded.Length}{Environment.NewLine}");
            _failedPayloadDumpWritten = true;
        }
    }

    private static (int? chunkX, int? chunkZ) ResolveChunkCoordinates(
        string regionFileName,
        MinecraftXbox360RegionChunk chunk,
        int? chunkX,
        int? chunkZ)
    {
        if (chunkX is not null && chunkZ is not null)
        {
            return (chunkX, chunkZ);
        }

        if (!TryParseRegionCoordinates(regionFileName, out int regionX, out int regionZ))
        {
            return (chunkX, chunkZ);
        }

        return (regionX * 32 + chunk.X, regionZ * 32 + chunk.Z);
    }

    private static bool TryParseRegionCoordinates(string regionFileName, out int regionX, out int regionZ)
    {
        regionX = 0;
        regionZ = 0;

        Match match = RegionNamePattern.Match(regionFileName);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["x"].Value, out regionX)
            || !int.TryParse(match.Groups["z"].Value, out regionZ))
        {
            regionX = 0;
            regionZ = 0;
            return false;
        }

        return true;
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
