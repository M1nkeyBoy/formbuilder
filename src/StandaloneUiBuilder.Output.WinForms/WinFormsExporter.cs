using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.WinForms;

/// <summary>
/// Writes a generated WinForms project to disk, in a folder named after its namespace inside
/// the folder the user chose. Exporting again regenerates the Designer and event files and
/// leaves every other existing file, including the developer's code, untouched.
/// </summary>
public static class WinFormsExporter
{
    public static string ProjectFolderFor(ProjectDocument document, string parentFolder) =>
        Path.Combine(parentFolder, CodeNames.ToNamespace(document.Name));

    public static ExportResult Export(ProjectDocument document, string parentFolder)
    {
        if (WinFormsGenerator.Check(document) is { Count: > 0 } problems)
        {
            throw new ExportException(string.Join(Environment.NewLine, problems));
        }

        return ProjectExporter.Export(
            ProjectFolderFor(document, parentFolder),
            guardedFile: $"{WinFormsGenerator.FormClassName}.Designer.cs",
            namespaceSource: $"{WinFormsGenerator.FormClassName}.cs",
            defaultNamespace: CodeNames.ToNamespace(document.Name),
            rootNamespace => WinFormsGenerator.Generate(document, rootNamespace));
    }
}
