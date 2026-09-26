using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

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
}
