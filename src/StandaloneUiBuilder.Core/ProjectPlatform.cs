namespace StandaloneUiBuilder.Core;

/// <summary>
/// The platform a project's screens are for. It decides what Export writes, and which control
/// libraries the project can use: a library's controls exist on one platform only.
/// </summary>
public enum ProjectPlatform
{
    /// <summary>Any platform: exports to all five, with the built-in controls only.</summary>
    Any,

    Wpf,

    WinForms,

    WinUI,

    Maui,

    Blazor,
}

public static class ProjectPlatforms
{
    /// <summary>Every platform, in the order the builder lists them.</summary>
    public static IReadOnlyList<ProjectPlatform> All { get; } =
        [ProjectPlatform.Wpf, ProjectPlatform.WinForms, ProjectPlatform.WinUI, ProjectPlatform.Maui, ProjectPlatform.Blazor, ProjectPlatform.Any];

    /// <summary>The name the user sees.</summary>
    public static string DisplayName(this ProjectPlatform platform) => platform switch
    {
        ProjectPlatform.Wpf => "WPF",
        ProjectPlatform.WinForms => "Windows Forms",
        ProjectPlatform.WinUI => "WinUI 3",
        ProjectPlatform.Maui => ".NET MAUI",
        ProjectPlatform.Blazor => "Blazor",
        _ => "Any platform",
    };

    /// <summary>A sentence on what the platform is, for the new-project picker.</summary>
    public static string Description(this ProjectPlatform platform) => platform switch
    {
        ProjectPlatform.Wpf => "Windows desktop apps with XAML. The designer draws library controls as they will look.",
        ProjectPlatform.WinForms => "Classic Windows desktop apps, laid out in code.",
        ProjectPlatform.WinUI => "Modern Windows apps with the Windows App SDK.",
        ProjectPlatform.Maui => "One app for Windows, Android, iOS and macOS.",
        ProjectPlatform.Blazor => "Web pages in the browser, with Razor components.",
        _ => "Export to all five with the built-in controls. Control libraries need one platform.",
    };

    /// <summary>True if the project can be exported to the given platform.</summary>
    public static bool CanExportTo(this ProjectPlatform platform, ProjectPlatform target) =>
        platform == ProjectPlatform.Any || platform == target;
}
