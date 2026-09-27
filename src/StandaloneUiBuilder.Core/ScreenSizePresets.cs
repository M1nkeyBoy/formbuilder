namespace StandaloneUiBuilder.Core;

/// <summary>A named screen size, such as a phone's, in DIPs (the same as CSS pixels).</summary>
public sealed record ScreenSizePreset(string Name, string Category, int Width, int Height)
{
    public string Label => $"{Name} ({Width} × {Height})";
}

/// <summary>
/// Popular device and desktop sizes the screen can be set to, as browsers' developer tools
/// offer them: phones and tablets in portrait, in CSS pixels, and common desktop resolutions.
/// </summary>
public static class ScreenSizePresets
{
    public const string Phones = "Phones";
    public const string Tablets = "Tablets";
    public const string Desktops = "Desktops";

    public static IReadOnlyList<ScreenSizePreset> All { get; } =
    [
        new("iPhone SE", Phones, 375, 667),
        new("iPhone 12 / 13 / 14", Phones, 390, 844),
        new("iPhone 15 Pro / 16", Phones, 393, 852),
        new("iPhone 14 Pro Max", Phones, 430, 932),
        new("Pixel 7", Phones, 412, 915),
        new("Samsung Galaxy S8+", Phones, 360, 740),
        new("Samsung Galaxy S20 Ultra", Phones, 412, 915),
        new("Samsung Galaxy A51 / A71", Phones, 412, 914),
        new("iPad Mini", Tablets, 768, 1024),
        new("iPad Air", Tablets, 820, 1180),
        new("iPad Pro 12.9\"", Tablets, 1024, 1366),
        new("Surface Pro 7", Tablets, 912, 1368),
        new("Samsung Galaxy Tab S4", Tablets, 712, 1138),
        new("Default window", Desktops, ScreenDocument.DefaultWidth, ScreenDocument.DefaultHeight),
        new("XGA", Desktops, 1024, 768),
        new("HD 720p", Desktops, 1280, 720),
        new("WXGA", Desktops, 1280, 800),
        new("Laptop HD", Desktops, 1366, 768),
        new("WXGA+", Desktops, 1440, 900),
        new("Laptop, Full HD at 125%", Desktops, 1536, 864),
        new("HD+", Desktops, 1600, 900),
        new("Full HD 1080p", Desktops, 1920, 1080),
        new("QHD 1440p", Desktops, 2560, 1440),
    ];

    /// <summary>The first preset with exactly this size; null for a custom size.</summary>
    public static ScreenSizePreset? Find(int width, int height) =>
        All.FirstOrDefault(p => p.Width == width && p.Height == height);
}
