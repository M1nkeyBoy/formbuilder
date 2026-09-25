using System.Globalization;
using System.Text.RegularExpressions;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Colours are stored as "#RRGGBB" in upper case, a form every target understands: WPF and CSS
/// read it as is, and WinForms builds it from its red, green and blue parts.
/// </summary>
public static partial class ControlColor
{
    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex CanonicalPattern();

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex InputPattern();

    public static bool IsCanonical(string color) => CanonicalPattern().IsMatch(color);

    /// <summary>
    /// Reads a colour typed by the user: blank means none (null); "1e6fd9" and "#1E6FD9" both
    /// mean #1E6FD9. Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, out string? color)
    {
        color = null;
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!InputPattern().IsMatch(trimmed))
        {
            return false;
        }

        color = "#" + trimmed.TrimStart('#').ToUpperInvariant();
        return true;
    }

    /// <summary>The red, green and blue parts of a canonical colour.</summary>
    public static (int Red, int Green, int Blue) Parts(string color)
    {
        var value = int.Parse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }
}
