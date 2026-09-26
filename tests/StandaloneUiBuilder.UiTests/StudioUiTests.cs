using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Microsoft.Win32;

namespace StandaloneUiBuilder.UiTests;

/// <summary>The redesigned editor: toolbox search, Layers, zoom and the dark editor.</summary>
public sealed class StudioUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    private static string Sample => Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj");

    [UiWalkthroughFact]
    public void SearchFiltersTheToolboxAndLayersSelectControls()
    {
        using var session = EditorSession.Launch(recoveryDirectory, Sample);
        _ = session.Window;
        session.Screenshot("36-studio");

        // Searching leaves only the matching tiles.
        session.TypeInto("ControlSearchBox", "slid");
        var toolbox = session.ById("ToolboxList");
        EditorSession.WaitUntil(() => toolbox.FindFirstDescendant(session.Find.ByName("TextBox")) is null, () => "TextBox is still in the toolbox.");
        Assert.NotNull(toolbox.FindFirstDescendant(session.Find.ByName("Slider")));
        session.Field("ControlSearchBox").Text = "";
        EditorSession.WaitUntil(() => toolbox.FindFirstDescendant(session.Find.ByName("TextBox")) is not null, () => "The toolbox did not show every control again.");

        // Layers lists the screen's controls; choosing one selects it.
        session.Window.FindFirstDescendant(session.Find.ByName("Layers").And(session.Find.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem)))!.Click();
        var layers = session.ById("LayersList");
        EditorSession.Within(layers, session.Find.ByName("LogoImage"), "the LogoImage layer").Click();
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "LogoImage", () => $"Selected {session.Field("NameBox").Text}");
        session.Screenshot("36-layers");
    }

    [UiWalkthroughFact]
    public void ClicksLandOnTheRightControlAtAnyZoom()
    {
        using var session = EditorSession.Launch(recoveryDirectory, Sample);
        _ = session.Window;
        var surface = session.ById("Surface");
        var width = surface.BoundingRectangle.Width;

        session.ById("ZoomInButton").AsButton().Invoke();
        EditorSession.WaitUntil(() => session.ById("ZoomText").Name == "125%", () => $"Zoom: {session.ById("ZoomText").Name}");
        EditorSession.WaitUntil(() => Math.Abs(surface.BoundingRectangle.Width - width * 1.25) <= 2, () => $"The canvas is {surface.BoundingRectangle.Width} wide, not {width * 1.25}.");

        // The logo image is at (20, 390) and 160 × 120 in the design: click its middle, scaled.
        var box = surface.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(box.Left + (int)(100 * 1.25), box.Top + (int)(450 * 1.25)));
        Wait.UntilInputIsProcessed();
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "LogoImage", () => $"Selected {session.Field("NameBox").Text}");
        session.Screenshot("36-zoomed");

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        EditorSession.WaitUntil(() => session.ById("ZoomText").Name == "100%", () => $"Zoom: {session.ById("ZoomText").Name}");
    }

    /// <summary>With Windows set to dark apps, the editor is dark too (the setting is put back afterwards).</summary>
    [UiWalkthroughFact]
    public void TheEditorFollowsWindowsDarkMode()
    {
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var key = Registry.CurrentUser.CreateSubKey(Personalize);
        var before = key.GetValue("AppsUseLightTheme");
        key.SetValue("AppsUseLightTheme", 0, RegistryValueKind.DWord);
        try
        {
            using var session = EditorSession.Launch(recoveryDirectory, Sample);
            _ = session.Window;
            Thread.Sleep(500);
            session.Screenshot("36-dark-editor");

            // The inspector's panel, beside the canvas, is dark.
            var panel = session.ById("ScreenNameBox");
            var point = new System.Drawing.Point(panel.BoundingRectangle.Left - 8, panel.BoundingRectangle.Top);
            using var capture = Capture.Rectangle(new System.Drawing.Rectangle(point.X, point.Y, 1, 1));
            var pixel = capture.Bitmap.GetPixel(0, 0);
            Assert.True((pixel.R + pixel.G + pixel.B) / 3 < 80, $"The inspector is not dark: {pixel}.");
        }
        finally
        {
            if (before is null)
            {
                key.DeleteValue("AppsUseLightTheme", throwOnMissingValue: false);
            }
            else
            {
                key.SetValue("AppsUseLightTheme", before, RegistryValueKind.DWord);
            }
        }
    }
}
