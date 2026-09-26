using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// A control library the project uses: a NuGet package for the project's platform, and the
/// controls the builder found in it. The controls are stored with the project, so it opens
/// and exports without downloading the package again.
/// </summary>
public sealed record LibraryPackage
{
    /// <summary>The NuGet package ID, such as "Syncfusion.SfGrid.WPF".</summary>
    public required string Id { get; init; }

    /// <summary>The package version the exported project references.</summary>
    public required string Version { get; init; }

    /// <summary>The package's controls, as found in its assemblies, by type name.</summary>
    public ImmutableList<LibraryControl> Controls { get; init; } = ImmutableList<LibraryControl>.Empty;
}

/// <summary>A control from a library: its type and the properties the builder can set.</summary>
public sealed record LibraryControl
{
    /// <summary>The full type name without generic arity, such as "Syncfusion.UI.Xaml.Grid.SfDataGrid".</summary>
    public required string TypeName { get; init; }

    /// <summary>The name of the assembly that defines the type, which XAML namespaces refer to.</summary>
    public required string Assembly { get; init; }

    /// <summary>The size a new one gets, in DIPs.</summary>
    public int Width { get; init; } = 160;

    public int Height { get; init; } = 40;

    /// <summary>The type parameters of a generic component (Blazor), such as TValue; null for none.</summary>
    public ImmutableList<string>? TypeParameters { get; init; }

    /// <summary>The properties the builder can set, in the order the inspector lists them.</summary>
    public ImmutableList<LibraryProperty> Properties { get; init; } = ImmutableList<LibraryProperty>.Empty;

    /// <summary>The type's own name, such as "SfDataGrid".</summary>
    [JsonIgnore]
    public string Name => TypeName[(TypeName.LastIndexOf('.') + 1)..];

    /// <summary>The type's namespace, such as "Syncfusion.UI.Xaml.Grid".</summary>
    [JsonIgnore]
    public string Namespace => TypeName.Contains('.', StringComparison.Ordinal) ? TypeName[..TypeName.LastIndexOf('.')] : "";
}

/// <summary>A property of a library control that the builder can set.</summary>
public sealed record LibraryProperty
{
    public required string Name { get; init; }

    /// <summary>
    /// The CLR type: System.String, System.Boolean, a number type such as System.Double, or an
    /// enum's full name with its members in <see cref="Choices"/>. System.Type stands for a
    /// generic component's type argument.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>An enum's members; null for other types.</summary>
    public ImmutableList<string>? Choices { get; init; }

    [JsonIgnore]
    public LibraryValueKind Kind => LibraryValues.KindOf(Type, Choices);
}

/// <summary>A value set on a library control: stored with its type, so export needs nothing else.</summary>
public sealed record LibrarySetting
{
    public required string Name { get; init; }

    /// <summary>The property's CLR type, as in <see cref="LibraryProperty.Type"/>.</summary>
    public required string Type { get; init; }

    /// <summary>The value as invariant text: "True", "12.5", an enum member's name, or text.</summary>
    public required string Value { get; init; }

    [JsonIgnore]
    public LibraryValueKind Kind => LibraryValues.KindOf(Type, null);
}

/// <summary>How the builder edits and writes a library property's value.</summary>
public enum LibraryValueKind
{
    Text,
    Flag,

    /// <summary>A whole number: Int32, Int64, Int16, Byte and their unsigned forms.</summary>
    Whole,

    /// <summary>A number with a fraction: Double, Single or Decimal.</summary>
    Number,

    /// <summary>An enum member.</summary>
    Choice,

    /// <summary>A generic component's type argument, such as string or DateTime.</summary>
    TypeArgument,
}

public static class LibraryValues
{
    private static readonly HashSet<string> WholeTypes =
        ["System.Int32", "System.Int64", "System.Int16", "System.Byte", "System.UInt32", "System.UInt64", "System.UInt16", "System.SByte"];

    private static readonly HashSet<string> NumberTypes = ["System.Double", "System.Single", "System.Decimal"];

    /// <summary>True for the CLR types a library property may have besides enums.</summary>
    public static bool IsSimpleType(string type) =>
        type is "System.String" or "System.Boolean" or "System.Type" || WholeTypes.Contains(type) || NumberTypes.Contains(type);

    public static LibraryValueKind KindOf(string type, ImmutableList<string>? choices) => type switch
    {
        "System.String" => LibraryValueKind.Text,
        "System.Boolean" => LibraryValueKind.Flag,
        "System.Type" => LibraryValueKind.TypeArgument,
        _ when WholeTypes.Contains(type) => LibraryValueKind.Whole,
        _ when NumberTypes.Contains(type) => LibraryValueKind.Number,
        _ => LibraryValueKind.Choice,
    };

    /// <summary>
    /// The value in its stored form, or an error the user can act on. Text is kept as typed;
    /// on and off become True and False; numbers must fit the property's type.
    /// </summary>
    public static (string? Value, string? Error) Parse(LibraryProperty property, string text)
    {
        var trimmed = text.Trim();
        switch (property.Kind)
        {
            case LibraryValueKind.Text:
                return (text, null);
            case LibraryValueKind.Flag:
                return bool.TryParse(trimmed, out var flag) ? (flag ? "True" : "False", null) : (null, $"{property.Name} is on or off: True or False.");
            case LibraryValueKind.Whole:
                return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) && FitsWhole(property.Type, whole)
                    ? (whole.ToString(CultureInfo.InvariantCulture), null)
                    : (null, $"{property.Name} must be a whole number{WholeRange(property.Type)}.");
            case LibraryValueKind.Number:
                return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                    ? (number.ToString("R", CultureInfo.InvariantCulture), null)
                    : (null, $"{property.Name} must be a number, such as 12.5.");
            case LibraryValueKind.TypeArgument:
                return System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z_][A-Za-z0-9_.]*(\?)?$")
                    ? (trimmed, null)
                    : (null, $"{property.Name} must be a type, such as string or DateTime.");
            default:
                var choice = property.Choices?.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
                return choice is not null ? (choice, null) : (null, $"{property.Name} must be one of: {string.Join(", ", property.Choices ?? [])}.");
        }
    }

    private static bool FitsWhole(string type, long value) => type switch
    {
        "System.Int32" => value is >= int.MinValue and <= int.MaxValue,
        "System.Int16" => value is >= short.MinValue and <= short.MaxValue,
        "System.Byte" => value is >= 0 and <= byte.MaxValue,
        "System.SByte" => value is >= sbyte.MinValue and <= sbyte.MaxValue,
        "System.UInt16" => value is >= 0 and <= ushort.MaxValue,
        "System.UInt32" => value is >= 0 and <= uint.MaxValue,
        "System.UInt64" => value >= 0,
        _ => true,
    };

    private static string WholeRange(string type) => type switch
    {
        "System.Byte" => " from 0 to 255",
        "System.UInt16" or "System.UInt32" or "System.UInt64" => " of 0 or more",
        _ => "",
    };

    /// <summary>The library control a control places, looked up in the project's libraries.</summary>
    public static LibraryControl? Find(ProjectDocument document, string? typeName) =>
        typeName is null ? null : document.Libraries?.SelectMany(l => l.Controls).FirstOrDefault(c => c.TypeName == typeName);

    /// <summary>The package a library control comes from.</summary>
    public static LibraryPackage? PackageOf(ProjectDocument document, string? typeName) =>
        typeName is null ? null : document.Libraries?.FirstOrDefault(l => l.Controls.Any(c => c.TypeName == typeName));

    /// <summary>The name of the vendor whose licence key a package needs, if the builder knows how to register it.</summary>
    public static string? LicensedVendor(string packageId) =>
        packageId.StartsWith("Syncfusion.", StringComparison.OrdinalIgnoreCase) ? "Syncfusion" : null;
}
