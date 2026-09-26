using System.Collections.Immutable;

namespace StandaloneUiBuilder.Core;

/// <summary>Commands for control libraries and the controls placed from them.</summary>
public sealed partial class DesignEditor
{
    /// <summary>
    /// Adds a library to the project, or updates it to another version. A library needs a
    /// platform, since its controls exist on one platform only. Values set on existing controls
    /// that the new version no longer has are dropped.
    /// </summary>
    public string? AddLibrary(LibraryPackage package)
    {
        if (Document.Platform == ProjectPlatform.Any)
        {
            return "Choose the project's platform first (Project > Platform): a library's controls exist on one platform only.";
        }

        if (package.Controls.Count == 0)
        {
            return $"{package.Id} has no controls for {Document.Platform.DisplayName()}.";
        }

        var libraries = Document.Libraries ?? ImmutableList<LibraryPackage>.Empty;
        var index = libraries.FindIndex(l => string.Equals(l.Id, package.Id, StringComparison.OrdinalIgnoreCase));
        var next = Document with { Libraries = index < 0 ? libraries.Add(package) : libraries.SetItem(index, package) };
        next = next with { Screens = next.Screens.ConvertAll(screen => screen with { Controls = KeepKnownSettings(next, screen.Controls) }) };
        Commit(next);
        return null;
    }

    /// <summary>Removes a library the project no longer uses. Controls placed from it must be deleted first.</summary>
    public string? RemoveLibrary(string packageId)
    {
        if (Document.Libraries?.FirstOrDefault(l => string.Equals(l.Id, packageId, StringComparison.OrdinalIgnoreCase)) is not { } package)
        {
            return $"The project does not use {packageId}.";
        }

        var types = package.Controls.Select(c => c.TypeName).ToHashSet(StringComparer.Ordinal);
        var users = Document.Screens
            .SelectMany(s => ControlTree.All(s.Controls).Where(c => c.Properties.LibraryType is { } type && types.Contains(type)).Select(c => (Screen: s, Control: c)))
            .ToList();
        if (users.Count > 0)
        {
            var names = string.Join(", ", users.Take(5).Select(u => Document.Screens.Count > 1 ? $"{u.Control.Name} ({u.Screen.Name})" : u.Control.Name));
            return $"{package.Id} is still used by {names}{(users.Count > 5 ? $" and {users.Count - 5} more" : "")}. Delete them first.";
        }

        var remaining = Document.Libraries!.Remove(package);
        Commit(Document with { Libraries = remaining.Count > 0 ? remaining : null });
        return null;
    }

    /// <summary>Sets or clears (with empty text) a vendor's licence key, which every export registers.</summary>
    public bool SetLicenseKey(string vendor, string key)
    {
        key = key.Trim();
        var keys = Document.LicenseKeys ?? ImmutableSortedDictionary<string, string>.Empty;
        var current = keys.GetValueOrDefault(vendor) ?? "";
        if (current == key)
        {
            return false;
        }

        keys = key.Length == 0 ? keys.Remove(vendor) : keys.SetItem(vendor, key);
        Commit(Document with { LicenseKeys = keys.Count > 0 ? keys : null });
        return true;
    }

    /// <summary>Places a library control on the screen, at a screen point snapped to the grid.</summary>
    public ControlDocument? AddLibraryControl(string typeName, double x, double y)
    {
        if (LibraryValues.Find(Document, typeName) is not { } library)
        {
            return null;
        }

        var screen = Screen;
        var width = Math.Min(library.Width, screen.Width);
        var height = Math.Min(library.Height, screen.Height);
        var control = NewLibraryControl(screen, library).WithBounds(new ControlBounds(
            Math.Clamp(DesignGeometry.Snap(x, screen.GridSize), 0, screen.Width - width),
            Math.Clamp(DesignGeometry.Snap(y, screen.GridSize), 0, screen.Height - height),
            width,
            height));
        Commit(Document.WithScreen(screen with { Controls = screen.Controls.Add(control) }));
        return control;
    }

    /// <summary>Places a library control inside a container, where a screen point falls.</summary>
    public ControlDocument? AddLibraryControlTo(string typeName, Guid containerId, double x, double y)
    {
        var screen = Screen;
        if (LibraryValues.Find(Document, typeName) is not { } library
            || Placed(containerId) is not { } container || !ControlCatalog.Get(container.Control.Type).CanHold(ControlType.Custom))
        {
            return null;
        }

        var control = NewLibraryControl(screen, library);
        var (index, row, column) = DropPosition(container, x, y, ignore: null);
        control = control with { Row = row, Column = column };
        Commit(Document.WithScreen(screen with { Controls = ControlTree.Insert(screen.Controls, containerId, index, control) }));
        return control;
    }

    /// <summary>
    /// Sets one of a library control's values from what the user typed; empty text clears it,
    /// so the control uses the library's own default.
    /// </summary>
    public string? SetLibrarySetting(Guid id, string name, string text)
    {
        if (FindControl(id) is not { Type: ControlType.Custom } control)
        {
            return "The control no longer exists.";
        }

        var library = LibraryValues.Find(Document, control.Properties.LibraryType);
        var property = library?.Properties.FirstOrDefault(p => p.Name == name)
            ?? (library?.TypeParameters?.Contains(name) == true ? new LibraryProperty { Name = name, Type = "System.Type" } : null);
        if (property is null)
        {
            return $"{control.Name} has no property {name}.";
        }

        var settings = control.Properties.LibrarySettings ?? ImmutableList<LibrarySetting>.Empty;
        var existing = settings.FirstOrDefault(s => s.Name == name);
        if (text.Length == 0 || (property.Kind != LibraryValueKind.Text && text.Trim().Length == 0))
        {
            if (existing is null)
            {
                return null;
            }

            settings = settings.Remove(existing);
        }
        else
        {
            var (value, error) = LibraryValues.Parse(property, text);
            if (error is not null)
            {
                return error;
            }

            if (existing?.Value == value)
            {
                return null;
            }

            var setting = new LibrarySetting { Name = name, Type = property.Type, Value = value! };
            settings = existing is null
                ? settings.Insert(InspectorOrder(property, control, settings), setting)
                : settings.Replace(existing, setting);
        }

        Replace(control, control with { Properties = control.Properties with { LibrarySettings = settings.Count > 0 ? settings : null } });
        return null;
    }

    /// <summary>How many controls from libraries the project has.</summary>
    public int LibraryControlCount => Document.Screens.Sum(s => ControlTree.All(s.Controls).Count(c => c.Type == ControlType.Custom));

    /// <summary>Settings are kept in the order the inspector lists the properties.</summary>
    private int InspectorOrder(LibraryProperty property, ControlDocument control, ImmutableList<LibrarySetting> settings)
    {
        var library = LibraryValues.Find(Document, control.Properties.LibraryType)!;
        var order = (library.TypeParameters ?? []).Concat(library.Properties.Select(p => p.Name)).ToList();
        var position = order.IndexOf(property.Name);
        var index = settings.FindIndex(s => order.IndexOf(s.Name) > position);
        return index < 0 ? settings.Count : index;
    }

    private static ControlDocument NewLibraryControl(ScreenDocument screen, LibraryControl library)
    {
        var taken = new HashSet<string>(ControlTree.All(screen.Controls).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var stem = char.ToUpperInvariant(library.Name[0]) + library.Name[1..];
        var name = Enumerable.Range(1, int.MaxValue).Select(n => $"{stem}{n}").First(n => !taken.Contains(n));
        var settings = library.TypeParameters?.Select(t => new LibrarySetting { Name = t, Type = "System.Type", Value = DefaultTypeArgument(t) }).ToImmutableList();
        return new ControlDocument
        {
            Id = Guid.NewGuid(),
            Type = ControlType.Custom,
            Name = name,
            Properties = new ControlProperties
            {
                LibraryType = library.TypeName,
                LibraryAssembly = library.Assembly,
                LibrarySettings = settings is { Count: > 0 } ? settings : null,
            },
        }.WithBounds(new ControlBounds(0, 0, library.Width, library.Height));
    }

    /// <summary>
    /// A generic component's first type argument: on or off for a TChecked, as check boxes and
    /// switches have it; text otherwise, which suits most inputs and lists. The user changes it.
    /// </summary>
    private static string DefaultTypeArgument(string name) =>
        name.Contains("Checked", StringComparison.OrdinalIgnoreCase) ? "bool" : "string";

    /// <summary>Drops values the library control no longer has, after its library changed.</summary>
    private static ImmutableList<ControlDocument> KeepKnownSettings(ProjectDocument document, ImmutableList<ControlDocument> controls) =>
        controls.ConvertAll(control =>
        {
            var children = control.Children is { } kids ? KeepKnownSettings(document, kids) : null;
            if (control.Type != ControlType.Custom || LibraryValues.Find(document, control.Properties.LibraryType) is not { } library
                || control.Properties.LibrarySettings is not { } settings)
            {
                return children is null ? control : control with { Children = children };
            }

            var known = settings.Where(s => library.Properties.Any(p => p.Name == s.Name && p.Type == s.Type)
                || (library.TypeParameters?.Contains(s.Name) == true && s.Type == "System.Type")).ToImmutableList();
            return control with
            {
                Children = children,
                Properties = control.Properties with { LibrarySettings = known.Count > 0 ? known : null },
            };
        });

    /// <summary>
    /// The project's library controls and libraries, dropped: a change of platform takes them
    /// away, since they exist on their platform only.
    /// </summary>
    private static ProjectDocument WithoutLibraries(ProjectDocument document) => document with
    {
        Libraries = null,
        Screens = document.Screens.ConvertAll(screen => screen with
        {
            Controls = ControlTree.Remove(screen.Controls,
                ControlTree.All(screen.Controls).Where(c => c.Type == ControlType.Custom).Select(c => c.Id).ToList()),
        }),
    };
}
