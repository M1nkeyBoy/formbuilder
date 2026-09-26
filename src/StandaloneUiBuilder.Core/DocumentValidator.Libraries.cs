using System.Globalization;
using System.Text.RegularExpressions;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Rules for control libraries and the controls placed from them. Their names and values are
/// written into generated code, so a hand-edited file cannot slip anything else in.
/// </summary>
public static partial class DocumentValidator
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex DottedNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.\-]*$")]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.+\-]*$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*\??$")]
    private static partial Regex TypeArgumentPattern();

    /// <summary>True for a full type name such as "Syncfusion.UI.Xaml.Grid.SfDataGrid".</summary>
    public static bool IsDottedName(string? name) => name is not null && DottedNamePattern().IsMatch(name);

    private static void ValidateLibraries(ProjectDocument document, List<string> errors)
    {
        foreach (var (vendor, key) in document.LicenseKeys ?? System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty)
        {
            if (vendor is null || !NamePattern().IsMatch(vendor) || string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl))
            {
                errors.Add($"The licence key for \"{vendor}\" is not valid.");
            }
        }

        if (document.Libraries is not { } libraries)
        {
            return;
        }

        if (document.Platform == ProjectPlatform.Any && libraries.Count > 0)
        {
            errors.Add("The project uses control libraries but is for any platform; a library needs a platform.");
        }

        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            if (library.Id is null || !PackageIdPattern().IsMatch(library.Id) || library.Version is null || !VersionPattern().IsMatch(library.Version))
            {
                errors.Add($"The library \"{library.Id}\" {library.Version} does not have a valid package ID and version.");
                continue;
            }

            foreach (var control in library.Controls ?? [])
            {
                if (!IsDottedName(control.TypeName) || control.Assembly is null || !PackageIdPattern().IsMatch(control.Assembly))
                {
                    errors.Add($"Library {library.Id}: \"{control.TypeName}\" is not a valid control type.");
                    continue;
                }

                if (!types.Add(control.TypeName))
                {
                    errors.Add($"Library {library.Id}: {control.TypeName} is listed more than once.");
                }

                if (control.Width <= 0 || control.Height <= 0)
                {
                    errors.Add($"Library {library.Id}: {control.TypeName} has no valid size.");
                }

                foreach (var name in control.TypeParameters ?? [])
                {
                    if (name is null || !NamePattern().IsMatch(name))
                    {
                        errors.Add($"Library {library.Id}: {control.TypeName} has a type parameter \"{name}\" that is not a name.");
                    }
                }

                foreach (var property in control.Properties ?? [])
                {
                    if (property?.Name is null || !NamePattern().IsMatch(property.Name) || !IsDottedName(property.Type)
                        || (!LibraryValues.IsSimpleType(property.Type) && property.Choices is not { Count: > 0 })
                        || property.Choices?.Any(c => c is null || !NamePattern().IsMatch(c)) == true)
                    {
                        errors.Add($"Library {library.Id}: {control.TypeName} has a property \"{property?.Name}\" that is not valid.");
                    }
                }
            }
        }
    }

    private static void ValidateLibraryControl(ControlDocument control, List<string> errors)
    {
        var properties = control.Properties;
        if (!IsDottedName(properties.LibraryType) || properties.LibraryAssembly is null || !PackageIdPattern().IsMatch(properties.LibraryAssembly))
        {
            errors.Add($"Control \"{control.Name}\" does not name a valid library control type.");
            return;
        }

        foreach (var setting in properties.LibrarySettings ?? [])
        {
            if (setting?.Name is null || !NamePattern().IsMatch(setting.Name) || !IsDottedName(setting.Type) || setting.Value is null
                || !ValidSettingValue(setting))
            {
                errors.Add($"Control \"{control.Name}\": the value of \"{setting?.Name}\" is not valid.");
            }
        }
    }

    private static bool ValidSettingValue(LibrarySetting setting) => setting.Kind switch
    {
        LibraryValueKind.Text => true,
        LibraryValueKind.Flag => setting.Value is "True" or "False",
        LibraryValueKind.Whole => long.TryParse(setting.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
        LibraryValueKind.Number => double.TryParse(setting.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n),
        LibraryValueKind.TypeArgument => TypeArgumentPattern().IsMatch(setting.Value),
        _ => NamePattern().IsMatch(setting.Value),
    };
}
