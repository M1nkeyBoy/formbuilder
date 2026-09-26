using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output;

/// <summary>
/// What every target writes for control libraries: the package references, the licence keys
/// registered when the app starts, and the text of library controls' values in XAML and C#.
/// </summary>
public static class LibraryCode
{
    /// <summary>
    /// The files libraries add to an export: Directory.Build.props, which MSBuild reads for
    /// every project in the folder, with the package references (so a project exported before
    /// the libraries were added gets them too), and the licence registration.
    /// </summary>
    public static IEnumerable<GeneratedFile> Files(ProjectDocument document, string rootNamespace)
    {
        if (document.Libraries is not { Count: > 0 } libraries)
        {
            yield break;
        }

        var props = new StringBuilder();
        props.AppendLine($"<!-- {ProjectExporter.GeneratedMarker}. This file is replaced on every export: it references the");
        props.AppendLine("     project's control libraries. Add your own packages to the .csproj file. -->");
        props.AppendLine("<Project>");
        props.AppendLine("  <ItemGroup>");
        foreach (var (id, version) in PackageReferences(document))
        {
            props.AppendLine($"    <PackageReference Include=\"{id}\" Version=\"{version}\" />");
        }

        props.AppendLine("  </ItemGroup>");
        props.AppendLine("</Project>");
        yield return new GeneratedFile("Directory.Build.props", props.ToString(), Regenerate: true);

        if (LicenseRegistrations(document).ToList() is { Count: > 0 } registrations)
        {
            var code = new StringBuilder();
            code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
            code.AppendLine("// It registers the control libraries' licence keys (Project > Libraries) when the app starts.");
            code.AppendLine();
            code.AppendLine($"namespace {rootNamespace};");
            code.AppendLine();
            code.AppendLine("internal static class LibraryLicenses");
            code.AppendLine("{");
            code.AppendLine("#pragma warning disable CA2255 // Runs before any window or page, as the libraries require.");
            code.AppendLine("    [System.Runtime.CompilerServices.ModuleInitializer]");
            code.AppendLine("#pragma warning restore CA2255");
            code.AppendLine("    internal static void Register()");
            code.AppendLine("    {");
            foreach (var registration in registrations)
            {
                code.AppendLine($"        {registration}");
            }

            code.AppendLine("    }");
            code.AppendLine("}");
            yield return new GeneratedFile("LibraryLicenses.g.cs", code.ToString(), Regenerate: true);
        }
    }

    /// <summary>
    /// The packages an export references: the libraries, and what they need besides. Syncfusion's
    /// Blazor components need one of its themes, from a package of its own.
    /// </summary>
    public static IEnumerable<(string Id, string Version)> PackageReferences(ProjectDocument document)
    {
        var libraries = document.Libraries ?? [];
        foreach (var library in libraries)
        {
            yield return (library.Id, library.Version);
        }

        if (document.Platform == ProjectPlatform.Blazor && SyncfusionBlazor(document) is { } syncfusion
            && !libraries.Any(l => string.Equals(l.Id, "Syncfusion.Blazor.Themes", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("Syncfusion.Blazor.Themes", syncfusion.Version);
        }
    }

    private static LibraryPackage? SyncfusionBlazor(ProjectDocument document) =>
        document.Libraries?.FirstOrDefault(l => l.Id.StartsWith("Syncfusion.Blazor", StringComparison.OrdinalIgnoreCase));

    private static bool UsesSyncfusionMaui(ProjectDocument document) =>
        document.Libraries?.Any(l => l.Id.StartsWith("Syncfusion.Maui.", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Blazor: LibrarySetup.g.cs, whose AddLibraries adds the services the libraries need, called
    /// from Program.cs; written on every export, so a library added later is set up too.
    /// </summary>
    public static string BlazorSetupCode(ProjectDocument document, string rootNamespace)
    {
        var syncfusion = SyncfusionBlazor(document) is not null;
        var code = new StringBuilder();
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine("// It sets up the services the project's control libraries need; Program.cs calls AddLibraries.");
        code.AppendLine();
        if (syncfusion)
        {
            code.AppendLine("using Syncfusion.Blazor;");
            code.AppendLine();
        }

        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine("internal static class LibrarySetup");
        code.AppendLine("{");
        code.AppendLine("    public static IServiceCollection AddLibraries(this IServiceCollection services)");
        code.AppendLine("    {");
        if (syncfusion)
        {
            code.AppendLine("        services.AddSyncfusionBlazor();");
        }

        code.AppendLine("        return services;");
        code.AppendLine("    }");
        code.AppendLine("}");
        return code.ToString();
    }

    /// <summary>Blazor: the stylesheets the libraries need, in App.razor's head.</summary>
    public static string BlazorHead(ProjectDocument document) =>
        $"@* {ProjectExporter.GeneratedMarker}. This file is replaced on every export: the control libraries' stylesheets. *@{Environment.NewLine}"
        + (SyncfusionBlazor(document) is not null ? $"<link href=\"_content/Syncfusion.Blazor.Themes/fluent2.css\" rel=\"stylesheet\" />{Environment.NewLine}" : "");

    /// <summary>Blazor: the scripts the libraries need, at the end of App.razor's body.</summary>
    public static string BlazorScripts(ProjectDocument document) =>
        $"@* {ProjectExporter.GeneratedMarker}. This file is replaced on every export: the control libraries' scripts. *@{Environment.NewLine}"
        + (SyncfusionBlazor(document) is not null ? $"<script src=\"_content/Syncfusion.Blazor.Core/scripts/syncfusion-blazor.min.js\" type=\"text/javascript\"></script>{Environment.NewLine}" : "");

    /// <summary>
    /// .NET MAUI: LibrarySetup.g.cs, whose UseLibraries sets up the libraries' handlers, called
    /// from MauiProgram.cs; written on every export.
    /// </summary>
    public static string MauiSetupCode(ProjectDocument document, string rootNamespace)
    {
        var syncfusion = UsesSyncfusionMaui(document);
        var code = new StringBuilder();
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine("// It sets up the project's control libraries; MauiProgram.cs calls UseLibraries.");
        code.AppendLine();
        if (syncfusion)
        {
            code.AppendLine("using Syncfusion.Maui.Core.Hosting;");
            code.AppendLine();
        }

        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine("internal static class LibrarySetup");
        code.AppendLine("{");
        code.AppendLine("    public static MauiAppBuilder UseLibraries(this MauiAppBuilder builder)");
        code.AppendLine("    {");
        if (syncfusion)
        {
            code.AppendLine("        builder.ConfigureSyncfusionCore();");
        }

        code.AppendLine("        return builder;");
        code.AppendLine("    }");
        code.AppendLine("}");
        return code.ToString();
    }

    /// <summary>The statements that register the licence keys of the vendors whose libraries the project uses.</summary>
    public static IEnumerable<string> LicenseRegistrations(ProjectDocument document)
    {
        var vendors = (document.Libraries ?? []).Select(l => LibraryValues.LicensedVendor(l.Id)).OfType<string>().ToHashSet();
        foreach (var (vendor, key) in document.LicenseKeys ?? System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty)
        {
            if (vendors.Contains(vendor) && vendor == "Syncfusion")
            {
                yield return $"Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense({ViewModelCode.Literal(key)});";
            }
        }
    }

    /// <summary>A XAML namespace for each .NET namespace the screen's library controls come from.</summary>
    public sealed record XamlNamespace(string Prefix, string Namespace, string Assembly);

    /// <summary>
    /// The XAML namespaces a screen's library controls need, with short prefixes from the .NET
    /// namespace's first part: "syncfusion", then "syncfusion2" for a second namespace.
    /// </summary>
    public static IReadOnlyList<XamlNamespace> XamlNamespaces(ScreenDocument screen)
    {
        var used = ControlTree.All(screen.Controls)
            .Where(c => c.Type == ControlType.Custom && c.Properties.LibraryType is { Length: > 0 })
            .Select(c => (Namespace: NamespaceOf(c.Properties.LibraryType!), Assembly: c.Properties.LibraryAssembly ?? ""))
            .Distinct()
            .OrderBy(n => n.Namespace, StringComparer.Ordinal).ThenBy(n => n.Assembly, StringComparer.Ordinal)
            .ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<XamlNamespace>();
        foreach (var (ns, assembly) in used)
        {
            var stem = ns.Split('.')[0].ToLowerInvariant();
            if (stem is "x" or "d" or "mc" or "local" or "xml" || stem.Length == 0)
            {
                stem = "lib";
            }

            counts[stem] = counts.GetValueOrDefault(stem) + 1;
            result.Add(new XamlNamespace(counts[stem] == 1 ? stem : stem + counts[stem].ToString(CultureInfo.InvariantCulture), ns, assembly));
        }

        return result;
    }

    /// <summary>The element name of a library control in XAML, such as "syncfusion:SfDataGrid".</summary>
    public static string XamlElement(ScreenDocument screen, ControlDocument control)
    {
        var type = control.Properties.LibraryType ?? "";
        var ns = XamlNamespaces(screen).First(n => n.Namespace == NamespaceOf(type) && n.Assembly == (control.Properties.LibraryAssembly ?? ""));
        return $"{ns.Prefix}:{NameOf(type)}";
    }

    /// <summary>A value as XAML attribute text (not yet escaped): True, False, a number or an enum member.</summary>
    public static string XamlValue(LibrarySetting setting) => setting.Value;

    /// <summary>A value as a C# expression of the property's type.</summary>
    public static string CSharpValue(LibrarySetting setting) => setting.Kind switch
    {
        LibraryValueKind.Text => ViewModelCode.Literal(setting.Value),
        LibraryValueKind.Flag => setting.Value == "True" ? "true" : "false",
        LibraryValueKind.Whole => setting.Type switch
        {
            "System.Int64" => setting.Value + "L",
            "System.UInt32" => setting.Value + "U",
            "System.UInt64" => setting.Value + "UL",
            "System.Int32" => setting.Value,
            _ => $"({CSharpTypeName(setting.Type)}){setting.Value}",
        },
        LibraryValueKind.Number => setting.Type switch
        {
            "System.Single" => setting.Value + "f",
            "System.Decimal" => setting.Value + "m",
            _ => setting.Value.Contains('.', StringComparison.Ordinal) || setting.Value.Contains('E', StringComparison.OrdinalIgnoreCase)
                ? setting.Value
                : setting.Value + ".0",
        },
        LibraryValueKind.TypeArgument => setting.Value,
        _ => $"global::{setting.Type}.{setting.Value}",
    };

    private static string CSharpTypeName(string type) => type switch
    {
        "System.Byte" => "byte",
        "System.SByte" => "sbyte",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        _ => "global::" + type,
    };

    /// <summary>The settings written as properties: not a generic component's type arguments.</summary>
    public static IEnumerable<LibrarySetting> Values(ControlDocument control) =>
        (control.Properties.LibrarySettings ?? []).Where(s => s.Kind != LibraryValueKind.TypeArgument);

    /// <summary>A generic component's type arguments, such as TValue="string".</summary>
    public static IEnumerable<LibrarySetting> TypeArguments(ControlDocument control) =>
        (control.Properties.LibrarySettings ?? []).Where(s => s.Kind == LibraryValueKind.TypeArgument);

    /// <summary>A problem for each library control that cannot be exported.</summary>
    public static IEnumerable<string> Check(ProjectDocument document, ScreenDocument screen)
    {
        foreach (var control in ControlTree.All(screen.Controls).Where(c => c.Type == ControlType.Custom))
        {
            if (LibraryValues.Find(document, control.Properties.LibraryType) is null)
            {
                yield return $"\"{control.Name}\" is a {NameOf(control.Properties.LibraryType ?? "")} from a library the project no longer has. "
                    + "Add the library again (Project > Libraries), or delete the control.";
            }
        }
    }

    public static string NamespaceOf(string typeName) => typeName.Contains('.', StringComparison.Ordinal) ? typeName[..typeName.LastIndexOf('.')] : "";

    public static string NameOf(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];
}
