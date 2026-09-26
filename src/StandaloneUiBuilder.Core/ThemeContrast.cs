namespace StandaloneUiBuilder.Core;

/// <summary>
/// Keeps text readable where the design sets a background. In the dark theme (and the system
/// theme, which may be dark) text takes the theme's light colour, which would be lost on a
/// light background the design chose. So a control that shows text on a background of the
/// design's own (the control's, or else the nearest container's) gets black or white text,
/// whichever stands out, unless it has a text colour of its own. A control inside such a
/// container also takes the container's background, since some themes' text boxes and buttons
/// are see-through and others are not; this way it looks the same everywhere. The editor and
/// every output apply this to a screen before drawing or writing it.
/// </summary>
public static class ThemeContrast
{
    public static ScreenDocument Apply(ProjectTheme theme, ScreenDocument screen) =>
        theme == ProjectTheme.Light
            ? screen
            : screen with { Controls = screen.Controls.ConvertAll(c => Apply(c, behind: null)) };

    /// <summary>Black on a light colour, white on a dark one.</summary>
    public static string ContrastingText(string background)
    {
        var (red, green, blue) = ControlColor.Parts(background);
        return 0.299 * red + 0.587 * green + 0.114 * blue >= 140 ? "#000000" : "#FFFFFF";
    }

    /// <param name="behind">The design's background behind the control, if there is one.</param>
    private static ControlDocument Apply(ControlDocument control, string? behind)
    {
        var properties = control.Properties;
        var definition = ControlCatalog.Get(control.Type);
        var surface = properties.Background ?? behind;
        if (surface is not null && definition.HasFont)
        {
            control = control with
            {
                Properties = properties with
                {
                    Background = properties.Background ?? (definition.HasBackground ? behind : null),
                    Foreground = properties.Foreground ?? ContrastingText(surface),
                },
            };
        }

        // A TabControl's pages are drawn on its frame, in the theme's colours.
        var inside = control.Type == ControlType.TabControl ? null : properties.Background ?? behind;
        return control.Children is { } children
            ? control with { Children = children.ConvertAll(child => Apply(child, inside)) }
            : control;
    }
}
