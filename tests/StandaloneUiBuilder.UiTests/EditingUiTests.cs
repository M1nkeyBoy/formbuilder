using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Multi-select, group move, copy and paste, nudging, selection box and screen size.</summary>
public sealed class EditingUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void GroupEditingWorksWithMouseAndKeyboard()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");

        // Two buttons, one above the other.
        foreach (var (x, y) in new[] { (100, 100), (100, 200) })
        {
            EditorSession.Within(toolbox, session.Find.ByName("Button"), "toolbox Button").Click();
            session.ClickCanvas(x, y);
        }

        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Button2", () => "Buttons were not placed.");

        // Ctrl+click adds the first button to the selection.
        Keyboard.Press(VirtualKeyShort.CONTROL);
        session.ClickCanvas(150, 115);
        Keyboard.Release(VirtualKeyShort.CONTROL);
        EditorSession.WaitUntil(() => session.Status == "2 controls selected", () => $"Status: {session.Status}");

        // Dragging one selected control moves both.
        session.DragOnCanvas(150, 115, 200, 115);
        EditorSession.WaitUntil(() => session.Status == "Moved 2 controls", () => $"Status after group move: {session.Status}");
        AssertX(session, 200, 215, "150");
        AssertX(session, 200, 115, "150");

        // Arrow keys nudge the selected control: 1 DIP, or one grid step with Shift.
        EditorSession.Press(VirtualKeyShort.RIGHT);
        EditorSession.Press(VirtualKeyShort.RIGHT);
        EditorSession.Press(VirtualKeyShort.SHIFT, VirtualKeyShort.DOWN);
        EditorSession.WaitUntil(() => session.Field("XBox").Text == "152" && session.Field("YBox").Text == "110",
            () => $"Nudged to {session.Field("XBox").Text}, {session.Field("YBox").Text}.");

        // Duplicate makes a renamed copy one grid step down and right.
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_D);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Button3" && session.Field("XBox").Text == "162",
            () => $"Duplicate: {session.Field("NameBox").Text} at {session.Field("XBox").Text}.");

        // A selection box across all three selects them; Delete removes them in one step.
        session.DragOnCanvas(20, 20, 400, 300);
        EditorSession.WaitUntil(() => session.Status == "3 controls selected", () => $"Status after selection box: {session.Status}");
        EditorSession.Press(VirtualKeyShort.DELETE);
        EditorSession.WaitUntil(() => session.Status == "Deleted 3 controls", () => $"Status after delete: {session.Status}");

        // One undo brings all three back.
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        session.ClickCanvas(20, 400);
        session.DragOnCanvas(20, 20, 400, 300);
        EditorSession.WaitUntil(() => session.Status == "3 controls selected", () => $"Status after undo: {session.Status}");

        // With nothing selected, the Properties panel edits the screen size.
        session.ClickCanvas(20, 400);
        session.TypeInto("ScreenWidthBox", "1000");
        EditorSession.WaitUntil(() => session.Field("ScreenWidthBox").Text == "1000" && session.Window.Title.Contains('●'),
            () => "Screen width was not applied.");
        session.TypeInto("ScreenWidthBox", "200");
        EditorSession.WaitUntil(() => session.ById("ScreenErrorText").Name.Contains("would be outside"), () => "No message for a screen too small for its controls.");
        EditorSession.Press(VirtualKeyShort.ESCAPE);
        EditorSession.WaitUntil(() => session.Field("ScreenWidthBox").Text == "1000", () => "Esc did not restore the screen width.");
        session.Screenshot("09-group-editing");

        session.CloseWindow();
        session.DialogButton("Discard").Invoke();
        session.WaitForExit();
    }

    private static void AssertX(EditorSession session, int canvasX, int canvasY, string expectedX)
    {
        session.ClickCanvas(canvasX, canvasY);
        EditorSession.WaitUntil(() => session.Field("XBox").Text == expectedX, () => $"Control at {canvasX}, {canvasY} has X {session.Field("XBox").Text}, expected {expectedX}.");
    }
}
