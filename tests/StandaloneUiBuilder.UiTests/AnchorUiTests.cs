using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Setting anchors in the Properties panel and trying them in a resized Preview.</summary>
public sealed class AnchorUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void AnchoredButtonFollowsTheResizedPreview()
    {
        using var session = EditorSession.Launch(recoveryDirectory);

        // Add a Button and move it near the bottom-right corner.
        EditorSession.Within(session.ById("ToolboxList"), session.Find.ByName("Button"), "toolbox Button").Click();
        session.ClickCanvas(100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Button1", () => "Button was not placed.");
        session.TypeInto("XBox", "600");
        session.TypeInto("YBox", "500");

        // Anchor it to the right and bottom edges instead of the left and top.
        session.ById("AnchorRightBox").Click();
        session.ById("AnchorBottomBox").Click();
        session.ById("AnchorLeftBox").Click();
        session.ById("AnchorTopBox").Click();
        EditorSession.WaitUntil(() => IsChecked(session, "AnchorRightBox") && IsChecked(session, "AnchorBottomBox")
            && !IsChecked(session, "AnchorLeftBox") && !IsChecked(session, "AnchorTopBox"), () => "Anchors were not applied.");

        // Removing the last vertical anchor is refused and explained.
        session.ById("AnchorBottomBox").Click();
        EditorSession.WaitUntil(() => session.ById("InspectorErrorText").Name.Contains("Anchor to at least one"), () => "No message for an invalid anchor.");
        EditorSession.WaitUntil(() => IsChecked(session, "AnchorBottomBox"), () => "The invalid anchor change was not undone.");

        // In Preview, enlarge the surface with its grip; the button keeps its distance from the
        // right and bottom edges, so it moves by the same amount.
        session.ById("PreviewModeButton").Click();
        var scroller = session.ById("SurfaceScroller");
        scroller.Patterns.Scroll.Pattern.SetScrollPercent(100, 100);
        Thread.Sleep(300);

        var button = EditorSession.Within(scroller, session.Find.ByName("Button1").And(session.Find.ByControlType(UiaControlType.Button)), "preview button");
        var grip = EditorSession.Within(scroller, session.Find.ByName("Resize preview"), "resize grip");
        var before = button.BoundingRectangle;
        var gripCentre = grip.GetClickablePoint();

        EditorSession.Drag(gripCentre, new System.Drawing.Point(gripCentre.X + 120, gripCentre.Y + 60));

        var after = button.BoundingRectangle;
        session.Screenshot("07-anchored-preview");
        Assert.True(Math.Abs(after.Left - before.Left - 120) <= 3 && Math.Abs(after.Top - before.Top - 60) <= 3,
            $"The anchored button moved from {before} to {after}; expected +120, +60.");

        session.ById("DesignModeButton").Click();
        session.CloseWindow();
        session.DialogButton("Discard").Invoke();
        session.WaitForExit();
    }

    private static bool IsChecked(EditorSession session, string id) =>
        session.ById(id).AsCheckBox().IsChecked == true;
}
