using FlaUI.Core.Capturing;
using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Project > Theme through the real editor: the canvas shows the chosen theme.</summary>
public sealed class ThemeUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void ChoosingDarkDrawsTheCanvasDarkAndUndoGoesBack()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj");
        using var session = EditorSession.Launch(recoveryDirectory, sample);
        _ = session.Window;
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.NEXT);
        EditorSession.WaitUntil(() => session.Field("ScreenNameBox").Text == "Settings", () => $"Screen: {session.Field("ScreenNameBox").Text}");
        Assert.True(Brightness(session) > 200, "The canvas should start light.");

        // Project > Theme > Dark, from the keyboard.
        EditorSession.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_P);
        EditorSession.Press(VirtualKeyShort.KEY_T);
        EditorSession.Press(VirtualKeyShort.KEY_D);
        EditorSession.WaitUntil(() => session.Status == "Theme: dark", () => $"Status: {session.Status}");
        EditorSession.WaitUntil(() => Brightness(session) < 60, () => $"The canvas is not dark (brightness {Brightness(session)}).");
        session.Screenshot("31-dark-theme-design");

        session.ById("PreviewModeButton").Click();
        EditorSession.WaitUntil(() => Brightness(session) < 60, () => $"The Preview is not dark (brightness {Brightness(session)}).");
        session.Screenshot("31-dark-theme-preview");
        session.ById("DesignModeButton").Click();

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.WaitUntil(() => Brightness(session) > 200, () => $"Undo did not bring back the light canvas (brightness {Brightness(session)}).");
    }

    /// <summary>How bright the empty canvas is between grid lines, near its top-left corner (0 to 255).</summary>
    private static int Brightness(EditorSession session)
    {
        var point = session.Canvas(5, 5);
        using var capture = Capture.Rectangle(new System.Drawing.Rectangle(point.X, point.Y, 1, 1));
        var pixel = capture.Bitmap.GetPixel(0, 0);
        return (pixel.R + pixel.G + pixel.B) / 3;
    }
}
