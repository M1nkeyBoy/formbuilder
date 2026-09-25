using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.WinUI;

/// <summary>
/// Writes a generated WinUI 3 project to disk, in a folder named after its namespace inside the
/// folder the user chose. Exporting again regenerates each window's XAML and generated code and
/// leaves every other existing file, including the developer's code, untouched.
/// </summary>
public static class WinUIExporter
{
    public static string ProjectFolderFor(ProjectDocument document, string parentFolder) =>
        Path.Combine(parentFolder, CodeNames.ToNamespace(document.Name));

    public static ExportResult Export(ProjectDocument document, string parentFolder)
    {
        if (WinUIGenerator.Check(document) is { Count: > 0 } problems)
        {
            throw new ExportException(string.Join(Environment.NewLine, problems));
        }

        return ProjectExporter.Export(
            ProjectFolderFor(document, parentFolder),
            guardedFile: $"{WinUIGenerator.WindowClassName}.xaml",
            namespaceSource: $"{WinUIGenerator.WindowClassName}.xaml.cs",
            defaultNamespace: CodeNames.ToNamespace(document.Name),
            rootNamespace => WinUIGenerator.Generate(document, rootNamespace));
    }
}
