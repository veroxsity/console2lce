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

    public MinecraftXbox360ChunkDecoder()
    {
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
            new("XMemLzx_128k_128k"),
        ];

        public ChunkDecoderCandidate(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public byte[] Decode(byte[] compressedBytes, int expectedDecompressedSize, bool usesRleCompression)
        {
            return usesRleCompression
                ? XboxXMemDecompressService.DecompressLzxRleWithKnownContextVariants(compressedBytes, expectedDecompressedSize)
                : XboxXMemDecompressService.DecompressWithKnownContextVariants(compressedBytes, expectedDecompressedSize);
        }
    }
}
