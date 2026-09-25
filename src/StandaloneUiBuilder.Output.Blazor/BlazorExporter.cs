using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Blazor;

/// <summary>
/// Writes a generated Blazor project to disk, in a folder named after its namespace inside the
/// folder the user chose. Exporting again regenerates each screen's page markup and event code
/// and leaves every other existing file, including the developer's code, untouched.
/// </summary>
public static class BlazorExporter
{
    public static string ProjectFolderFor(ProjectDocument document, string parentFolder) =>
        Path.Combine(parentFolder, CodeNames.ToNamespace(document.Name));

    public static ExportResult Export(ProjectDocument document, string parentFolder)
    {
        if (BlazorGenerator.Check(document) is { Count: > 0 } problems)
        {
            throw new ExportException(string.Join(Environment.NewLine, problems));
        }

        return ProjectExporter.Export(
            ProjectFolderFor(document, parentFolder),
            guardedFile: BlazorGenerator.PagePath(BlazorGenerator.MainPageClassName, ".razor"),
            namespaceSource: BlazorGenerator.PagePath(BlazorGenerator.MainPageClassName, ".razor.cs"),
            defaultNamespace: CodeNames.ToNamespace(document.Name),
            rootNamespace => BlazorGenerator.Generate(document, rootNamespace),
            namespaceSuffix: BlazorGenerator.PagesNamespaceSuffix);
    }
}
