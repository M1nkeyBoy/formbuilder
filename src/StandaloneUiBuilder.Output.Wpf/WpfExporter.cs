using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Wpf;

/// <summary>
/// Writes a generated WPF project to disk. The project goes in a folder named after its
/// namespace inside the folder the user chose. Exporting again regenerates the window and its
/// event wiring and leaves every other existing file, including the developer's code, untouched.
/// </summary>
public static class WpfExporter
{
    public static string ProjectFolderFor(ProjectDocument document, string parentFolder) =>
        Path.Combine(parentFolder, CodeNames.ToNamespace(document.Name));

    public static ExportResult Export(ProjectDocument document, string parentFolder)
    {
        if (WpfGenerator.Check(document) is { Count: > 0 } problems)
        {
            throw new ExportException(string.Join(Environment.NewLine, problems));
        }

        return ProjectExporter.Export(
            ProjectFolderFor(document, parentFolder),
            guardedFile: $"{WpfGenerator.WindowClassName}.xaml",
            namespaceSource: $"{WpfGenerator.WindowClassName}.xaml.cs",
            defaultNamespace: CodeNames.ToNamespace(document.Name),
            rootNamespace => WpfGenerator.Generate(document, rootNamespace));
    }
}
