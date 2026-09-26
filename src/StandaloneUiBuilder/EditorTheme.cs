using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace StandaloneUiBuilder;

/// <summary>
/// The editor's own colours, light or dark as Windows is set to show apps, changing when that
/// setting does. This is separate from a project's theme, which is how its designs look.
/// </summary>
internal static class EditorTheme
{
    private static readonly Uri LightColors = new("pack://application:,,,/StandaloneUiBuilder;component/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkColors = new("pack://application:,,,/StandaloneUiBuilder;component/Themes/Dark.xaml", UriKind.Absolute);

    public static bool IsDark { get; private set; }

    /// <summary>Loads the colours to match Windows, and keeps them matching.</summary>
    public static void Start(Application app)
    {
        Apply(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                app.Dispatcher.BeginInvoke(() => Apply(app));
            }
        };

        // Every window gets a title bar to match, including dialogs.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => SetTitleBar((Window)sender)));
    }

    private static void Apply(Application app)
    {
        IsDark = WindowsAppsUseDarkMode();
        var colors = new ResourceDictionary { Source = IsDark ? DarkColors : LightColors };
        var merged = app.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source == LightColors || d.Source == DarkColors);
        if (existing is null)
        {
            merged.Insert(0, colors);
        }
        else if (existing.Source != colors.Source)
        {
            merged[merged.IndexOf(existing)] = colors;
        }

        foreach (Window window in app.Windows)
        {
            SetTitleBar(window);
        }
    }

    /// <summary>Whether Windows is set to show apps in dark mode.</summary>
    public static bool WindowsAppsUseDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    // The native title bar stays; Windows draws it dark on request (Windows 10 20H1 and later).
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void SetTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var dark = IsDark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));
        }
    }
}
