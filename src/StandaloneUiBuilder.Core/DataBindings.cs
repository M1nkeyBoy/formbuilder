using System.Text.RegularExpressions;

namespace StandaloneUiBuilder.Core;

/// <summary>A property of a screen's view model: its name, kind, and the controls bound to it, in screen order.</summary>
/// <param name="Controls">At least one; the first gives the property its starting value.</param>
public sealed record BoundProperty(string Name, BindingKind Kind, IReadOnlyList<ControlDocument> Controls)
{
    /// <summary>The control whose design value the property starts with: the first bound by its value.</summary>
    public ControlDocument First => Controls.FirstOrDefault(c => c.Properties.Binding == Name) ?? Controls[0];

    /// <summary>True if only buttons use the property, to be enabled by it; it then starts true.</summary>
    public bool OnlyEnables => Controls.All(c => c.Properties.Binding != Name);

    /// <summary>True if a control can change the value; false if they all only show it (Labels, ProgressBars).</summary>
    public bool IsSet => Controls.Any(c => !ControlCatalog.Get(c.Type).ShowsBindingOnly);
}

/// <summary>A partial method of a screen's view model that the screen's code can implement.</summary>
public sealed record ViewModelHook(string Name, string Description)
{
    /// <summary>An empty implementation to start from.</summary>
    public string Stub => $"partial void {Name}()\n{{\n}}\n";
}

/// <summary>A command of a screen's view model: its name and the buttons that run it, in screen order.</summary>
public sealed record BoundCommand(string Name, IReadOnlyList<ControlDocument> Buttons);

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
            .SelectMany(c => BoundNames(c).Select(b => (b.Name, b.Kind, Control: c)))
            .GroupBy(b => b.Name, StringComparer.Ordinal)
            .Select(g => new BoundProperty(g.Key, g.First().Kind, g.Select(b => b.Control).Distinct().ToList()))
            .ToList();

    /// <summary>
    /// The view model properties a control uses: its value's binding, and for a Button, the
    /// on-or-off property that enables it.
    /// </summary>
    public static IEnumerable<(string Name, BindingKind Kind)> BoundNames(ControlDocument control)
    {
        var definition = ControlCatalog.Get(control.Type);
        if (control.Properties.Binding is { } binding && definition.BindingKind is { } kind)
        {
            yield return (binding, kind);
        }

        if (control.Properties.EnabledBinding is { } enabled && definition.HasEnabledBinding)
        {
            yield return (enabled, BindingKind.Flag);
        }
    }

    /// <summary>The screen's view model commands, in the order their first buttons come.</summary>
    public static IReadOnlyList<BoundCommand> Commands(ScreenDocument screen) =>
        ControlTree.All(screen.Controls)
            .Where(c => c.Properties.Command is not null && ControlCatalog.Get(c.Type).HasCommand)
            .GroupBy(c => c.Properties.Command!, StringComparer.Ordinal)
            .Select(g => new BoundCommand(g.Key, g.ToList()))
            .ToList();

    /// <summary>True if any control on the screen is bound or runs a command, or the screen has code, so it has a view model.</summary>
    public static bool HasViewModel(ScreenDocument screen) =>
        !string.IsNullOrWhiteSpace(screen.Code)
        || ControlTree.All(screen.Controls).Any(c => c.Properties.Binding is not null || c.Properties.Command is not null || c.Properties.EnabledBinding is not null);

    /// <summary>
    /// The partial methods of the screen's view model its code can implement: each command's
    /// On… method, run when a button is clicked, and each property's On…Changed method, run
    /// when its value changes.
    /// </summary>
    public static IReadOnlyList<ViewModelHook> Hooks(ScreenDocument screen) =>
        [
            .. Commands(screen).Select(c => new ViewModelHook($"On{c.Name}", $"Runs when {string.Join(" or ", c.Buttons.Select(b => b.Name))} is clicked.")),
            .. Properties(screen).Select(p => new ViewModelHook($"On{p.Name}Changed", $"Runs when {p.Name} changes.")),
        ];

    /// <summary>True if the code implements the hook: a method of that name is declared in it.</summary>
    public static bool Implements(string? code, ViewModelHook hook) =>
        code is not null && Regex.IsMatch(code, $@"\bvoid\s+{hook.Name}\s*\(");

    /// <summary>Checks a proposed binding for a control; returns an error message or null.</summary>
    public static string? Validate(ScreenDocument screen, ControlDocument control, string name)
    {
        if (ControlCatalog.Get(control.Type).BindingKind is not { } kind)
        {
            return $"A {control.Type} has no value to bind.";
        }

        return CheckName(name, "A binding") ?? CheckProperty(screen, control, name, kind);
    }

    /// <summary>Checks a proposed on-or-off property to enable a Button; returns an error message or null.</summary>
    public static string? ValidateEnabled(ScreenDocument screen, ControlDocument control, string name)
    {
        if (!ControlCatalog.Get(control.Type).HasEnabledBinding)
        {
            return $"A {control.Type} is not enabled by a binding.";
        }

        return CheckName(name, "A binding") ?? CheckProperty(screen, control, name, BindingKind.Flag);
    }

    /// <summary>Whether a property of this kind fits beside the screen's other bindings and commands.</summary>
    private static string? CheckProperty(ScreenDocument screen, ControlDocument control, string name, BindingKind kind)
    {
        foreach (var other in ControlTree.All(screen.Controls).Where(c => c.Id != control.Id))
        {
            if (BoundNames(other).Any(b => b.Name == name && b.Kind != kind))
            {
                return $"\"{name}\" is bound to {other.Name}, a {other.Type}, whose value is not of the same kind.";
            }
        }

        // The property and its change hook; controls bound to the same name share them.
        return Clash(screen, control, [name, $"On{name}Changed"], (isCommand, other) => !isCommand && other == name);
    }

    /// <summary>Checks a proposed command for a Button; returns an error message or null.</summary>
    public static string? ValidateCommand(ScreenDocument screen, ControlDocument control, string name)
    {
        if (!ControlCatalog.Get(control.Type).HasCommand)
        {
            return $"A {control.Type} has no command.";
        }

        // The method and its hook; buttons that run the same command share them.
        return CheckName(name, "A command") ?? Clash(screen, control, [name, $"On{name}"], (isCommand, other) => isCommand && other == name);
    }

    private static string? CheckName(string name, string what)
    {
        if (!PropertyName().IsMatch(name))
        {
            return $"{what} must start with a capital letter and contain only letters, digits and underscores.";
        }

        return Reserved.Contains(name) || name.EndsWith("ViewModel", StringComparison.Ordinal)
            ? $"\"{name}\" is used by the generated view model; choose another name."
            : null;
    }

    /// <summary>
    /// Whether members the name would add to the view model clash with those of the screen's
    /// other bindings and commands, ignoring case (for targets that do); <paramref name="shared"/>
    /// says which other names are the same binding or command, whose members are shared.
    /// </summary>
    private static string? Clash(ScreenDocument screen, ControlDocument control, string[] members, Func<bool, string, bool> shared)
    {
        foreach (var other in ControlTree.All(screen.Controls).Where(c => c.Id != control.Id))
        {
            foreach (var (isCommand, otherName, otherMembers) in MembersOf(other))
            {
                if (shared(isCommand, otherName))
                {
                    continue;
                }

                if (otherMembers.FirstOrDefault(m => members.Contains(m, StringComparer.OrdinalIgnoreCase)) is { } taken)
                {
                    var owner = isCommand ? $"the command \"{otherName}\" of {other.Name}" : $"the binding \"{otherName}\" of {other.Name}";
                    return taken == otherName && string.Equals(members[0], otherName, StringComparison.OrdinalIgnoreCase) && members[0] != otherName
                        ? $"\"{members[0]}\" differs from {owner} only in case."
                        : $"\"{members[0]}\" would clash with {owner} in the view model.";
                }
            }
        }

        return null;
    }

    private static IEnumerable<(bool IsCommand, string Name, string[] Members)> MembersOf(ControlDocument control)
    {
        foreach (var (binding, _) in BoundNames(control))
        {
            yield return (false, binding, [binding, $"On{binding}Changed"]);
        }

        if (control.Properties.Command is { } command)
        {
            yield return (true, command, [command, $"On{command}"]);
        }
    }
}
