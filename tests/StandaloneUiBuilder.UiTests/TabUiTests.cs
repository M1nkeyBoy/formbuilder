using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>TabControls through the real editor: showing a tab and adding one.</summary>
public sealed class TabUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void ClickingATabShowsItsPageAndAddTabAddsOne()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");
        EditorSession.Within(toolbox, session.Find.ByName("TabControl"), "toolbox TabControl").Click();
        session.ClickCanvas(100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TabControl1", () => $"Selected {session.Field("NameBox").Text}");

        var shownTab = session.ById("ShownTabBox").AsComboBox();
        EditorSession.WaitUntil(() => shownTab.SelectedItem?.Text == "1. Tab 1", () => $"Shown tab: {shownTab.SelectedItem?.Text}");

        // The canvas draws the real tabs; clicking the second one shows its page. Where UI
        // Automation does not report the drawn tab, click where it is at 100% scaling.
        var canvas = session.ById("SurfaceScroller");
        var secondTab = canvas.FindFirstDescendant(session.Find.ByName("Tab 2"));
        var box = secondTab?.BoundingRectangle;
        Mouse.Click(box is { IsEmpty: false } r ? new System.Drawing.Point(r.X + r.Width / 2, r.Y + r.Height / 2) : session.Canvas(160, 111));
        Wait.UntilInputIsProcessed();
        EditorSession.WaitUntil(() => session.ById("ShownTabBox").AsComboBox().SelectedItem?.Text == "2. Tab 2",
            () => $"Shown tab: {session.ById("ShownTabBox").AsComboBox().SelectedItem?.Text}");

        // Clicking inside the page selects the page shown.
        session.ClickCanvas(250, 220);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TabPage2", () => $"Selected {session.Field("NameBox").Text}");

        session.ById("AddTabButton").AsButton().Invoke();
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TabPage3", () => $"Selected {session.Field("NameBox").Text}");
        EditorSession.WaitUntil(() => session.ById("ShownTabBox").AsComboBox().SelectedItem?.Text == "3. Tab 3",
            () => $"Shown tab: {session.ById("ShownTabBox").AsComboBox().SelectedItem?.Text}");
        session.Screenshot("29-tab-control");
    }

    /// <summary>
    /// The sample's Settings screen has a tab order of its own; Preview (built like the generated
    /// WPF window) follows it. Setting the order by clicking numbers the controls.
    /// </summary>
    [UiWalkthroughFact]
    public void PreviewFollowsTheTabOrderAndClicksSetIt()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj");
        var document = Core.ProjectFile.Load(sample);
        using var session = EditorSession.Launch(recoveryDirectory, sample);
        _ = session.Window;
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.NEXT);
        EditorSession.WaitUntil(() => session.Field("ScreenNameBox").Text == "Settings", () => $"Screen: {session.Field("ScreenNameBox").Text}");

        session.ById("PreviewModeButton").Click();
        session.ById("ServerTextBox").Focus();
        using var automation = new FlaUI.UIA3.UIA3Automation();
        TabOrderCheck.AssertTabOrder(automation, session.ById("SurfaceScroller"), document.Screens[1], focusWindow: false);

        // Back in Design, Ctrl+T numbers the controls; clicking the slider makes it first.
        session.ById("DesignModeButton").Click();
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
        EditorSession.WaitUntil(() => session.Status.StartsWith("Click the controls", StringComparison.Ordinal), () => $"Status: {session.Status}");
        session.ClickCanvas(450, 147);
        EditorSession.WaitUntil(() => session.Status.StartsWith("LevelSlider is number 1", StringComparison.Ordinal), () => $"Status: {session.Status}");
        session.Screenshot("30-tab-order");
        EditorSession.Press(VirtualKeyShort.ESCAPE);
        EditorSession.WaitUntil(() => session.Status == "Tab order set", () => $"Status: {session.Status}");
    }
}
