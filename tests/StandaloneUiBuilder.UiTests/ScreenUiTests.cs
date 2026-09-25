using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Working with several screens through the real editor.</summary>
public sealed class ScreenUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void ScreensCanBeAddedRenamedAndSwitched()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");
        var tabs = session.ById("ScreenTabs");
        void Place(string type, int x, int y)
        {
            EditorSession.Within(toolbox, session.Find.ByName(type), "toolbox " + type).Click();
            session.ClickCanvas(x, y);
        }

        // A button on the main screen.
        Place("Button", 100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Button1", () => "The button was not placed.");

        // Add a screen: it is empty and shown straight away, with its name in the Properties panel.
        session.ById("AddScreenButton").AsButton().Invoke();
        EditorSession.WaitUntil(() => session.Status.StartsWith("Added screen Screen2", StringComparison.Ordinal), () => $"Status: {session.Status}");
        Assert.Equal("Screen2", session.Field("ScreenNameBox").Text);

        session.TypeInto("ScreenNameBox", "Settings");
        EditorSession.Within(tabs, session.Find.ByName("Settings"), "the renamed Settings tab");

        // Default names restart on each screen.
        Place("CheckBox", 100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "CheckBox1", () => "The check box was not placed.");
        session.Screenshot("12-second-screen");

        // Back to the main screen by its tab: the button is there, the check box is not.
        EditorSession.Within(tabs, session.Find.ByName("Main"), "the Main tab").Click();
        EditorSession.WaitUntil(() => session.Status == "Screen Main", () => $"Status: {session.Status}");
        session.ClickCanvas(110, 110);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Button1", () => "The main screen's button is missing.");

        // Ctrl+PageDown shows the next screen.
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.NEXT);
        EditorSession.WaitUntil(() => session.Status == "Screen Settings", () => $"Status: {session.Status}");
        session.ClickCanvas(110, 110);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "CheckBox1", () => "The Settings screen's check box is missing.");

        // Undo removes the check box on this screen, then the rename, then the screen itself.
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.Within(tabs, session.Find.ByName("Screen2"), "the Screen2 tab after undoing the rename");
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.WaitUntil(() => tabs.FindAllChildren().Length == 1, () => $"{tabs.FindAllChildren().Length} tabs after undoing Add Screen.");
    }

    /// <summary>In Preview, the layout demo's OK button opens Settings and its Close button goes back.</summary>
    [UiWalkthroughFact]
    public void ButtonsOpenAndCloseScreensInPreview()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj");
        using var session = EditorSession.Launch(recoveryDirectory, sample);
        EditorSession.WaitUntil(() => session.Window.Title.StartsWith("layout-demo", StringComparison.Ordinal), () => "The sample did not open.");

        session.ById("PreviewModeButton").Click();
        EditorSession.WaitUntil(() => session.Status.StartsWith("Preview", StringComparison.Ordinal), () => $"Status: {session.Status}");

        // OK is the first button in the footer stack at (20, 530).
        session.ClickCanvas(70, 550);
        EditorSession.WaitUntil(() => session.Status == "OkButton clicked: opened Settings", () => $"Status: {session.Status}");
        session.Screenshot("14-preview-opened-settings");

        // Close sits at (530, 400) on the Settings screen.
        session.ClickCanvas(575, 415);
        EditorSession.WaitUntil(() => session.Status == "CloseSettingsButton clicked: closed Settings", () => $"Status: {session.Status}");
        Assert.True(session.Window.FindFirstDescendant(session.Find.ByName("OK")) is not null, "The Main screen is not showing again.");
    }
}
