using System.Text.RegularExpressions;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Wpf;

/// <summary>An export could not be completed. The message is suitable for the user.</summary>
public sealed class WpfExportException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>What an export did to each file.</summary>
public sealed record WpfExportResult(
    string ProjectFolder,
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Kept);

/// <summary>
/// Writes a generated WPF project to disk. The project goes in a folder named after its
/// namespace inside the folder the user chose. Exporting again regenerates MainWindow.xaml
/// and leaves every other existing file, including the developer's code, untouched.
/// </summary>
public static partial class WpfExporter
{
    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

    public static string ProjectFolderFor(ProjectDocument document, string parentFolder) =>
        Path.Combine(parentFolder, WpfGenerator.ToNamespace(document.Name));

    public static WpfExportResult Export(ProjectDocument document, string parentFolder)
    {
        if (WpfGenerator.Check(document) is { Count: > 0 } problems)
        {
            throw new WpfExportException(string.Join(Environment.NewLine, problems));
        }

        var folder = ProjectFolderFor(document, parentFolder);
        var created = new List<string>();
        var updated = new List<string>();
        var kept = new List<string>();

        try
        {
            Directory.CreateDirectory(folder);

            var windowPath = Path.Combine(folder, $"{WpfGenerator.WindowClassName}.xaml");
            if (File.Exists(windowPath) && !File.ReadAllText(windowPath).Contains(WpfGenerator.GeneratedMarker, StringComparison.Ordinal))
            {
                throw new WpfExportException(
                    $"\"{windowPath}\" was not created by Standalone UI Builder, so it was not replaced. "
                    + "Choose another folder, or move that file first.");
            }

            // Keep the namespace of an earlier export, even if the project has been renamed
            // since, so the regenerated window still matches the developer's code-behind.
            var rootNamespace = ExistingNamespace(folder) ?? WpfGenerator.ToNamespace(document.Name);

            foreach (var file in WpfGenerator.Generate(document, rootNamespace))
            {
                var path = Path.Combine(folder, file.RelativePath);
                var exists = File.Exists(path);
                if (exists && !file.Regenerate)
                {
                    kept.Add(file.RelativePath);
                    continue;
                }

                if (exists && File.ReadAllText(path) == file.Content)
                {
                    kept.Add(file.RelativePath);
                    continue;
                }

                WriteAtomically(path, file.Content);
                (exists ? updated : created).Add(file.RelativePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new WpfExportException($"Could not write the WPF project to \"{folder}\": {ex.Message}", ex);
        }

        return new WpfExportResult(folder, created, updated, kept);
    }

    private static string? ExistingNamespace(string folder)
    {
        var codeBehind = Path.Combine(folder, $"{WpfGenerator.WindowClassName}.xaml.cs");
        if (!File.Exists(codeBehind))
        {
            return null;
        }

        var match = NamespaceDeclaration().Match(File.ReadAllText(codeBehind));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void WriteAtomically(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
