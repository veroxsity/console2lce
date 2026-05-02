namespace Console2Lce.Cli;

internal static class ExtractCommandRunner
{
    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);

        Directory.CreateDirectory(outputPath);

        byte[] inputBytes = File.ReadAllBytes(inputPath);
        SavegameInputData input = SavegameInputReader.Read(inputBytes);
        SavegameDecodingResult decodeResult = new SavegameDecodeService().Decode(input.SavegameBytes, input.LeadingPrefixBytes);
        var layout = new DebugArtifactLayout(outputPath);

        File.WriteAllBytes(layout.SavegameDatPath, input.SavegameBytes);

        Console.WriteLine($"Input:   {inputPath}");
        Console.WriteLine($"Output:  {outputPath}");
        Console.WriteLine($"Kind:    {(input.IsStfsPackage ? "STFS package" : "savegame.dat")}");
        Console.WriteLine($"Wrote    {layout.SavegameDatPath}");
        Console.WriteLine($"Bytes:   {input.SavegameBytes.Length}");

        if (decodeResult.DecompressedBytes is null)
        {
            Console.WriteLine("Decode:  unresolved");
            return 0;
        }

        Minecraft360Archive archive = ArchiveArtifactWriter.Write(layout, decodeResult.DecompressedBytes);
        Console.WriteLine($"Decode:  {decodeResult.DecoderSummary}");
        Console.WriteLine($"Files:   {archive.Entries.Count}");
        Console.WriteLine($"Wrote    {layout.SavegameDecompressedPath}");
        Console.WriteLine($"Wrote    {layout.ArchiveIndexJsonPath}");
        Console.WriteLine($"Wrote    {layout.ArchiveDirectoryPath}");
        return 0;
    }
}
