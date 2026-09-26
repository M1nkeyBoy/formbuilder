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
        // Names are unique across the whole screen, including inside containers.
        foreach (var other in ControlTree.All(screen.Controls))
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

    /// <summary>Checks a proposed name for a screen; returns an error message or null.</summary>
    public static string? ValidateScreenName(ProjectDocument document, string screenId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Screen name cannot be empty.";
        }

        if (!NamePattern().IsMatch(name))
        {
            return "Screen name must start with a letter or underscore and contain only letters, digits and underscores.";
        }

        foreach (var other in document.Screens)
        {
            if (other.Id != screenId && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"Another screen is already named \"{other.Name}\".";
            }
        }

        return null;
    }

    /// <summary>Validates a whole document; returns every problem found.</summary>
    public static IReadOnlyList<string> Validate(ProjectDocument document)
    {
        var errors = new List<string>();

        if (document.ProjectId == Guid.Empty)
        {
            errors.Add("The project has no ID.");
        }

        if (document.Screens is not { Count: > 0 } screens)
        {
            errors.Add("The project has no screen.");
            return errors;
        }

        // Control IDs are unique across the whole project, screen IDs across its screens.
        var ids = new HashSet<Guid>();
        var screenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var screen in screens)
        {
            // With one screen, messages read as before; with several, they say which screen.
            var prefix = screens.Count > 1 ? $"Screen \"{screen.Name}\": " : "";
            var screenErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(screen.Id))
            {
                screenErrors.Add("The screen has no ID.");
            }
            else if (!screenIds.Add(screen.Id))
            {
                screenErrors.Add($"The screen has the same ID as another screen ({screen.Id}).");
            }

            if (ValidateScreenName(document, screen.Id, screen.Name) is { } nameError)
            {
                screenErrors.Add(nameError);
            }

            if (screen.Width <= 0 || screen.Height <= 0)
            {
                screenErrors.Add($"The screen size {screen.Width} × {screen.Height} is not valid.");
            }

            if (screen.GridSize <= 0)
            {
                screenErrors.Add($"The grid size {screen.GridSize} is not valid.");
            }

            foreach (var control in screen.Controls)
            {
                ValidateControl(screen, control, parent: null, ids, screenErrors);
            }

            if (screen.TabOrder is { } tabOrder)
            {
                var stops = ControlTree.All(screen.Controls).Where(TabSequence.IsTabStop).Select(c => c.Id).ToHashSet();
                if (tabOrder.Distinct().Count() != tabOrder.Count)
                {
                    screenErrors.Add("The tab order lists a control more than once.");
                }

                if (tabOrder.FirstOrDefault(id => !stops.Contains(id)) is var stray && stray != Guid.Empty)
                {
                    screenErrors.Add($"The tab order lists {stray}, which is not a control on the screen that takes input.");
                }
            }

            foreach (var bound in ControlTree.All(screen.Controls).Where(c => c.Properties.Binding is not null))
            {
                if (DataBindings.Validate(screen, bound, bound.Properties.Binding!) is { } bindingError)
                {
                    screenErrors.Add($"Control \"{bound.Name}\": {bindingError}");
                }
            }

            foreach (var button in ControlTree.All(screen.Controls).Where(c => c.Properties.EnabledBinding is not null))
            {
                if (DataBindings.ValidateEnabled(screen, button, button.Properties.EnabledBinding!) is { } enabledError)
                {
                    screenErrors.Add($"Control \"{button.Name}\": {enabledError}");
                }
            }

            foreach (var button in ControlTree.All(screen.Controls).Where(c => c.Properties.Command is not null))
            {
                if (DataBindings.ValidateCommand(screen, button, button.Properties.Command!) is { } commandError)
                {
                    screenErrors.Add($"Control \"{button.Name}\": {commandError}");
                }
            }

            foreach (var button in ControlTree.All(screen.Controls).Where(c => c.Properties.OpensScreen is not null || c.Properties.ClosesScreen == true))
            {
                if (button.Properties.OpensScreen is { } target && document.FindScreen(target) is null)
                {
                    screenErrors.Add($"Control \"{button.Name}\" opens a screen that does not exist (\"{target}\").");
                }

                if (button.Properties.OpensScreen is not null && button.Properties.ClosesScreen == true)
                {
                    screenErrors.Add($"Control \"{button.Name}\" both opens a screen and closes its own.");
                }
            }

            errors.AddRange(screenErrors.Select(e => prefix + e));
        }

        return errors;
    }

    private static void ValidateControl(ScreenDocument screen, ControlDocument control, ControlDocument? parent, HashSet<Guid> ids, List<string> errors)
    {
        var label = string.IsNullOrEmpty(control.Name) ? $"A {control.Type}" : $"Control \"{control.Name}\"";

        if (!ControlCatalog.TryGet(control.Type, out var definition))
        {
            errors.Add($"{label} has an unsupported type ({control.Type}).");
            return;
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

        if (parent is null && !definition.InToolbox)
        {
            errors.Add($"{label}: a {control.Type} must be inside a TabControl.");
        }
        else if (parent is not null && !ControlCatalog.Get(parent.Type).CanHold(control.Type))
        {
            errors.Add(parent.Type == ControlType.TabControl
                ? $"{label}: TabControl \"{parent.Name}\" can hold only tab pages."
                : $"{label}: a {control.Type} must be inside a TabControl.");
        }

        if (definition.IsTabs && control.Properties.SelectedTab is { } tab && (tab < 0 || tab >= Math.Max(1, control.Children?.Count ?? 0)))
        {
            errors.Add($"{label}: the tab shown ({tab + 1}) is not one of its tabs.");
        }

        if (parent is null)
        {
            // On the screen: position, size and anchors matter.
            if (ValidateBounds(screen, control.Type, control.Bounds) is { } boundsError)
            {
                errors.Add($"{label}: {boundsError}");
            }

            if (AnchorLayout.Validate(control.Anchor) is { } anchorError)
            {
                errors.Add($"{label}: {anchorError}");
            }

            if (control.Row is not null || control.Column is not null || control.RowSpan is not null || control.ColumnSpan is not null)
            {
                errors.Add($"{label}: only controls inside a Grid have a row, column and span.");
            }
        }
        else
        {
            // Inside a container, the container places the control; its own size is still used
            // along a stack's direction and must respect the type's minimum.
            if (control.Width < definition.MinWidth || control.Height < definition.MinHeight)
            {
                errors.Add($"{label}: its size must be at least {definition.MinWidth} × {definition.MinHeight}.");
            }

            var inGrid = parent.Type == ControlType.Grid;
            if (inGrid && (control.Row is not { } row || control.Column is not { } column
                || row < 0 || row >= (parent.Properties.Rows ?? 1) || column < 0 || column >= (parent.Properties.Columns ?? 1)))
            {
                errors.Add($"{label}: it needs a row and column inside Grid \"{parent.Name}\".");
            }
            else if (inGrid && (control.RowSpan is < 1 || control.ColumnSpan is < 1
                || (control.Row ?? 0) + (control.RowSpan ?? 1) > (parent.Properties.Rows ?? 1)
                || (control.Column ?? 0) + (control.ColumnSpan ?? 1) > (parent.Properties.Columns ?? 1)))
            {
                errors.Add($"{label}: its span goes beyond Grid \"{parent.Name}\".");
            }
            else if (!inGrid && (control.Row is not null || control.Column is not null || control.RowSpan is not null || control.ColumnSpan is not null))
            {
                errors.Add($"{label}: only controls inside a Grid have a row, column and span.");
            }
        }

        if (control.Properties.ImageData is { } image)
        {
            var imageError = ImageFile.TryDecode(image, out var bytes) ? ImageFile.Validate(bytes) : "The picture data is not valid base64.";
            if (imageError is not null)
            {
                errors.Add($"{label}: {imageError}");
            }
        }

        var style = control.Properties;
        if (style.FontSize is < ControlDefinition.MinFontSize or > ControlDefinition.MaxFontSize)
        {
            errors.Add($"{label}: text size must be between {ControlDefinition.MinFontSize} and {ControlDefinition.MaxFontSize}.");
        }

        foreach (var color in new[] { style.Foreground, style.Background })
        {
            if (color is not null && !ControlColor.IsCanonical(color))
            {
                errors.Add($"{label}: \"{color}\" is not a colour; colours are written #RRGGBB.");
            }
        }

        if (definition.HasRange && DesignEditor.ValidateRange(control.Properties.Minimum ?? 0, control.Properties.Maximum ?? 0, control.Properties.Value ?? 0) is { } rangeError)
        {
            errors.Add($"{label}: {rangeError}");
        }

        if (definition.IsContainer)
        {
            var properties = control.Properties;
            if (definition.IsStack && properties.Spacing is < 0 or > ControlDefinition.MaxSpacing)
            {
                errors.Add($"{label}: spacing must be between 0 and {ControlDefinition.MaxSpacing}.");
            }

            if (definition.IsGrid && (properties.Rows is < 1 or > ControlDefinition.MaxRowsOrColumns
                || properties.Columns is < 1 or > ControlDefinition.MaxRowsOrColumns))
            {
                errors.Add($"{label}: rows and columns must be between 1 and {ControlDefinition.MaxRowsOrColumns}.");
            }
            else if (definition.IsGrid)
            {
                if (properties.RowSizes is { } rowSizes && DesignEditor.ValidateTracks(rowSizes, properties.Rows ?? 1, "row") is { } rowError)
                {
                    errors.Add($"{label}: {rowError}");
                }

                if (properties.ColumnSizes is { } columnSizes && DesignEditor.ValidateTracks(columnSizes, properties.Columns ?? 1, "column") is { } columnError)
                {
                    errors.Add($"{label}: {columnError}");
                }
            }

            foreach (var child in control.Children ?? [])
            {
                ValidateControl(screen, child, control, ids, errors);
            }
        }
        else if (control.Children is { Count: > 0 })
        {
            errors.Add($"{label}: a {control.Type} cannot hold other controls.");
        }
    }
}
