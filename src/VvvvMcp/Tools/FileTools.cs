using System.ComponentModel;
using ModelContextProtocol.Server;

namespace VvvvMcp.Tools;

[McpServerToolType]
public static class FileTools
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vl", ".cs", ".csproj", ".sln", ".sdsl", ".hlsl",
        ".json", ".xml", ".txt", ".md", ".fsx", ".nuspec",
        ".config", ".props", ".targets", ".yaml", ".yml"
    };

    [McpServerTool(Name = "read_file")]
    [Description("Read the contents of a source file. Supports .vl, .cs, .csproj, .sdsl, .hlsl, .json, .xml, .md and other text files relevant to vvvv gamma development.")]
    public static object ReadFile(
        [Description("Absolute path to the file to read")] string filePath,
        [Description("Optional: start line (1-indexed, inclusive). Omit to read from beginning.")] int? startLine = null,
        [Description("Optional: end line (1-indexed, inclusive). Omit to read to end.")] int? endLine = null)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        var ext = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(ext))
            return new { error = $"Unsupported file type: {ext}. Allowed: {string.Join(", ", AllowedExtensions)}" };

        try
        {
            var lines = File.ReadAllLines(filePath);
            var totalLines = lines.Length;
            
            int start = Math.Max(0, (startLine ?? 1) - 1);
            int end = Math.Min(totalLines, endLine ?? totalLines);
            
            if (end - start > 500)
            {
                end = start + 500;
            }

            var content = string.Join("\n", lines[start..end]);

            return new
            {
                file = Path.GetFileName(filePath),
                path = filePath,
                extension = ext,
                totalLines,
                shownLines = new { from = start + 1, to = end },
                truncated = end < totalLines,
                content
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to read file: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "list_directory")]
    [Description("List files and subdirectories in a directory. Useful for exploring vvvv project structure.")]
    public static object ListDirectory(
        [Description("Absolute path to the directory")] string directoryPath,
        [Description("Optional file extension filter (e.g. '.vl', '.cs', '.sdsl')")] string? extensionFilter = null)
    {
        if (!Directory.Exists(directoryPath))
            return new { error = $"Directory not found: {directoryPath}" };

        try
        {
            var dirs = Directory.GetDirectories(directoryPath)
                .Select(d => new { name = Path.GetFileName(d), type = "directory" })
                .OrderBy(d => d.name);

            var filesQuery = Directory.GetFiles(directoryPath).AsEnumerable();
            if (!string.IsNullOrEmpty(extensionFilter))
            {
                filesQuery = filesQuery.Where(f => 
                    Path.GetExtension(f).Equals(extensionFilter, StringComparison.OrdinalIgnoreCase));
            }

            var files = filesQuery
                .Select(f => new 
                { 
                    name = Path.GetFileName(f), 
                    type = "file",
                    extension = Path.GetExtension(f),
                    sizeBytes = new FileInfo(f).Length
                })
                .OrderBy(f => f.name);

            return new
            {
                path = directoryPath,
                filter = extensionFilter,
                directories = dirs,
                files,
                totalDirectories = dirs.Count(),
                totalFiles = files.Count()
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to list directory: {ex.Message}" };
        }
    }
}
