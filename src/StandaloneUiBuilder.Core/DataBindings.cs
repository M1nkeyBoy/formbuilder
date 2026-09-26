using System.Text.RegularExpressions;

namespace StandaloneUiBuilder.Core;

/// <summary>A property of a screen's view model: its name, kind, and the controls bound to it, in screen order.</summary>
/// <param name="Controls">At least one; the first gives the property its starting value.</param>
public sealed record BoundProperty(string Name, BindingKind Kind, IReadOnlyList<ControlDocument> Controls)
{
    /// <summary>The control whose design value the property starts with.</summary>
    public ControlDocument First => Controls[0];

    /// <summary>True if a control can change the value; false if they all only show it (Labels, ProgressBars).</summary>
    public bool IsSet => Controls.Any(c => !ControlCatalog.Get(c.Type).ShowsBindingOnly);
}

/// <summary>
/// Data binding: a control's value can be bound to a named property of its screen's view model,
/// a class generated with the screen that raises a change notification when a property
/// changes. Controls on a screen that bind the same name share the property, so a Label can
/// show what a TextBox holds; their values must be of the same kind.
/// </summary>
public static partial class DataBindings
{
    // PascalCase, as properties are: never a C# keyword, which are all lower case.
    [GeneratedRegex("^[A-Z][A-Za-z0-9_]*$")]
    private static partial Regex PropertyName();

    /// <summary>Names the generated view model uses for itself.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "PropertyChanged", "OnPropertyChanged", "SetProperty" };

    /// <summary>The screen's view model properties, in the order their first controls come.</summary>
    public static IReadOnlyList<BoundProperty> Properties(ScreenDocument screen) =>
        ControlTree.All(screen.Controls)
            .Where(c => c.Properties.Binding is not null && ControlCatalog.Get(c.Type).BindingKind is not null)
            .GroupBy(c => c.Properties.Binding!, StringComparer.Ordinal)
            .Select(g => new BoundProperty(g.Key, ControlCatalog.Get(g.First().Type).BindingKind!.Value, g.ToList()))
            .ToList();

    /// <summary>True if any control on the screen is bound, so the screen has a view model.</summary>
    public static bool HasViewModel(ScreenDocument screen) =>
        ControlTree.All(screen.Controls).Any(c => c.Properties.Binding is not null);

    /// <summary>Checks a proposed binding for a control; returns an error message or null.</summary>
    public static string? Validate(ScreenDocument screen, ControlDocument control, string name)
    {
        if (ControlCatalog.Get(control.Type).BindingKind is not { } kind)
        {
            return $"A {control.Type} has no value to bind.";
        }

        if (!PropertyName().IsMatch(name))
        {
            return "A binding must start with a capital letter and contain only letters, digits and underscores.";
        }

        if (Reserved.Contains(name) || name.EndsWith("ViewModel", StringComparison.Ordinal))
        {
            return $"\"{name}\" is used by the generated view model; choose another name.";
        }

        // The view model's properties differ in more than case, for targets that ignore it.
        foreach (var other in ControlTree.All(screen.Controls))
        {
            if (other.Id == control.Id || other.Properties.Binding is not { } otherName)
            {
                continue;
            }

            if (otherName != name && string.Equals(otherName, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"\"{name}\" differs from the binding \"{otherName}\" only in case.";
            }

            if (otherName == name && ControlCatalog.Get(other.Type).BindingKind != kind)
            {
                return $"\"{name}\" is bound to {other.Name}, a {other.Type}, whose value is not of the same kind.";
            }
        }

        return null;
    }
}
