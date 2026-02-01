using System.CommandLine;
using AppKit;

namespace JpegXL.MacOS;

public record ProgramArgs
{
    public string? InputFile { get; init; }
    public string? ExportFormat { get; init; }
    public string? ExportFile { get; init; }
    public int? ExportFrameIndex { get; init; }
    public int? ExitAfterSeconds { get; init; }
}

static class Program
{
    // Capture original working directory before NSApplication changes it
    private static readonly string OriginalWorkingDirectory = Environment.CurrentDirectory;

    public static ProgramArgs Args { get; private set; } = new();

    /// <summary>
    /// Resolves a path relative to the original working directory (before app launch changed it).
    /// </summary>
    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(OriginalWorkingDirectory, path));
    }

    static int Main(string[] args)
    {
        Console.WriteLine($"[Program] Original working directory: {OriginalWorkingDirectory}");

        // Use string instead of FileInfo to avoid automatic path resolution issues
        var fileArg = new Argument<string?>("file")
        {
            Description = "JPEG XL file to open",
            Arity = ArgumentArity.ZeroOrOne
        };

        var exportAsOption = new Option<string?>("--export-as")
        {
            Description = "Export format: PNG, JPEG, or TIFF"
        };
        exportAsOption.Aliases.Add("-f");

        var exportFileOption = new Option<string?>("--export-file")
        {
            Description = "Export to this file path"
        };
        exportFileOption.Aliases.Add("-o");

        var exportFrameOption = new Option<int?>("--export-frame")
        {
            Description = "Frame number to export (1-based, for animations)"
        };

        var exitAfterOption = new Option<int?>("--exit-after")
        {
            Description = "Exit after N seconds (useful for automated testing)"
        };

        var rootCommand = new RootCommand("JPEG XL Viewer")
        {
            fileArg,
            exportAsOption,
            exportFileOption,
            exportFrameOption,
            exitAfterOption
        };

        rootCommand.SetAction((parseResult) =>
        {
            var file = parseResult.GetValue(fileArg);
            var exportAs = parseResult.GetValue(exportAsOption);
            var exportFile = parseResult.GetValue(exportFileOption);
            var exportFrame = parseResult.GetValue(exportFrameOption);
            var exitAfter = parseResult.GetValue(exitAfterOption);

            // Resolve paths relative to original working directory
            var inputFile = file != null ? ResolvePath(file) : null;
            var exportFilePath = exportFile != null ? ResolvePath(exportFile) : null;

            // Normalize and validate format
            string? exportFormat = null;
            if (exportAs != null)
            {
                exportFormat = exportAs.ToUpperInvariant() switch
                {
                    "PNG" => "PNG",
                    "JPEG" or "JPG" => "JPEG",
                    "TIFF" or "TIF" => "TIFF",
                    _ => throw new ArgumentException($"Unknown format: {exportAs}. Use PNG, JPEG, or TIFF")
                };
            }
            else if (exportFilePath != null)
            {
                // Infer format from extension
                var ext = Path.GetExtension(exportFilePath).ToLowerInvariant();
                exportFormat = ext switch
                {
                    ".png" => "PNG",
                    ".jpg" or ".jpeg" => "JPEG",
                    ".tiff" or ".tif" => "TIFF",
                    _ => throw new ArgumentException($"Cannot infer format from extension: {ext}. Use --export-as")
                };
            }

            if (exportFilePath != null && inputFile == null)
            {
                throw new ArgumentException("--export-file requires an input file");
            }

            Args = new ProgramArgs
            {
                InputFile = inputFile,
                ExportFormat = exportFormat,
                ExportFile = exportFilePath,
                ExportFrameIndex = exportFrame.HasValue ? exportFrame.Value - 1 : null, // Convert to 0-based
                ExitAfterSeconds = exitAfter
            };

            Console.WriteLine($"[Program] Input file: {Args.InputFile}");
            Console.WriteLine($"[Program] Export file: {Args.ExportFile}");

            NSApplication.Init();
            NSApplication.SharedApplication.Delegate = new AppDelegate();
            NSApplication.Main(args);
        });

        var parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }
}
