using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Maui;

/// <summary>
/// Writes a generated .NET MAUI project to disk, in a folder named after its namespace inside the
/// folder the user chose. Exporting again regenerates each page's XAML and generated code and
/// leaves every other existing file, including the developer's code, untouched.
/// </summary>
public static class MauiExporter
{
    public static string ProjectFolderFor(ProjectDocument document, string parentFolder) =>
        Path.Combine(parentFolder, CodeNames.ToNamespace(document.Name));

    public static ExportResult Export(ProjectDocument document, string parentFolder)
    {
        if (MauiGenerator.Check(document) is { Count: > 0 } problems)
        {
            throw new ExportException(string.Join(Environment.NewLine, problems));
        }

        return ProjectExporter.Export(
            ProjectFolderFor(document, parentFolder),
            guardedFile: $"{MauiGenerator.MainPageClassName}.xaml",
            namespaceSource: $"{MauiGenerator.MainPageClassName}.xaml.cs",
            defaultNamespace: CodeNames.ToNamespace(document.Name),
            rootNamespace => MauiGenerator.Generate(document, rootNamespace));
    }
}
