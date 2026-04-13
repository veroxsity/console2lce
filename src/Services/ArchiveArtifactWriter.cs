using System.Text.Json;

namespace Console2Lce;

public static class ArchiveArtifactWriter
{
    public static Minecraft360Archive Write(DebugArtifactLayout layout, ReadOnlyMemory<byte> decompressedBytes)
    {
        var parser = new Minecraft360ArchiveParser();
        Minecraft360Archive archive = parser.Parse(decompressedBytes);

        File.WriteAllBytes(layout.SavegameDecompressedPath, decompressedBytes.ToArray());
        File.WriteAllText(
            layout.ArchiveIndexJsonPath,
            JsonSerializer.Serialize(archive.Entries, new JsonSerializerOptions { WriteIndented = true }));

        foreach ((string name, byte[] bytes) in archive.Files)
        {
            string destinationPath = Path.Combine(layout.ArchiveDirectoryPath, name.Replace('/', Path.DirectorySeparatorChar));
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(destinationPath, bytes);
        }

        return archive;
    }
}
