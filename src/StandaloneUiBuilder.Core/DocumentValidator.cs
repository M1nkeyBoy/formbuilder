using System.Text.RegularExpressions;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Validation rules shared by editing (one value at a time) and loading (a whole document).
/// Every method returns plain-language messages, never exceptions.
/// </summary>
public static partial class DocumentValidator
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex NamePattern();

    /// <summary>Checks a proposed name for a control; returns an error message or null.</summary>
    public static string? ValidateName(ScreenDocument screen, Guid controlId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name cannot be empty.";
        }

        if (!NamePattern().IsMatch(name))
        {
            return "Name must start with a letter or underscore and contain only letters, digits and underscores.";
        }

        // Case-insensitive so names stay unique for any later case-insensitive output target.
        foreach (var other in screen.Controls)
        {
            if (other.Id != controlId && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"Another control is already named \"{other.Name}\".";
            }
        }

        return null;
    }

    /// <summary>Checks a proposed position and size; returns an error message or null.</summary>
    public static string? ValidateBounds(ScreenDocument screen, ControlType type, ControlBounds bounds)
    {
        var definition = ControlCatalog.Get(type);

        if (bounds.Width < definition.MinWidth)
        {
            return $"Width must be at least {definition.MinWidth} for a {type}.";
        }

        if (bounds.Height < definition.MinHeight)
        {
            return $"Height must be at least {definition.MinHeight} for a {type}.";
        }

        if (bounds.X < 0 || bounds.Y < 0)
        {
            return "X and Y cannot be negative.";
        }

        if (bounds.Right > screen.Width)
        {
            return $"X + Width cannot exceed the screen width ({screen.Width}).";
        }

        if (bounds.Bottom > screen.Height)
        {
            return $"Y + Height cannot exceed the screen height ({screen.Height}).";
        }

        return null;
    }

    /// <summary>Validates a whole document; returns every problem found.</summary>
    public static IReadOnlyList<string> Validate(ProjectDocument document)
    {
        var errors = new List<string>();
        var screen = document.Screen;

        if (document.ProjectId == Guid.Empty)
        {
            errors.Add("The project has no ID.");
        }

        if (screen is null)
        {
            errors.Add("The project has no screen.");
            return errors;
        }

        if (screen.Width <= 0 || screen.Height <= 0)
        {
            errors.Add($"The screen size {screen.Width} × {screen.Height} is not valid.");
        }

        if (screen.GridSize <= 0)
        {
            errors.Add($"The grid size {screen.GridSize} is not valid.");
        }

        var ids = new HashSet<Guid>();
        foreach (var control in screen.Controls)
        {
            var label = string.IsNullOrEmpty(control.Name) ? $"A {control.Type}" : $"Control \"{control.Name}\"";

            if (!ControlCatalog.TryGet(control.Type, out _))
            {
                errors.Add($"{label} has an unsupported type ({control.Type}).");
                continue;
            }

            if (control.Id == Guid.Empty)
            {
                errors.Add($"{label} has no ID.");
            }
            else if (!ids.Add(control.Id))
            {
                errors.Add($"{label} has the same ID as another control ({control.Id}).");
            }

            if (ValidateName(screen, control.Id, control.Name) is { } nameError)
            {
                errors.Add($"{label}: {nameError}");
            }

            if (ValidateBounds(screen, control.Type, control.Bounds) is { } boundsError)
            {
                errors.Add($"{label}: {boundsError}");
            }
        }

        return errors;
    }
}
