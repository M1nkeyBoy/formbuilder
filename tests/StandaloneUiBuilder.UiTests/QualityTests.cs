using System.Diagnostics;
using FlaUI.Core.WindowsAPI;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Checks for the spec's quality requirements that need the real UI.</summary>
public sealed class QualityTests : IDisposable
{
    private readonly string projectPath = Path.Combine(
        EditorSession.ArtifactsDirectory, $"quality-{Guid.NewGuid():N}.uibproj");

    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose()
    {
        File.Delete(projectPath);
        Directory.Delete(recoveryDirectory, recursive: true);
    }

    /// <summary>Placement, move and resize stay responsive on a screen with at least 50 controls.</summary>
    [UiWalkthroughFact]
    public void LargeScreenStaysResponsive()
    {
        // 60 controls: every type, in a 6 × 10 grid covering the screen.
        var editor = new DesignEditor();
        var types = ControlCatalog.All.Where(d => !d.IsContainer).Select(d => d.Type).ToArray();
        for (var i = 0; i < 60; i++)
        {
            editor.AddControl(types[i % types.Length], 10 + i % 6 * 130, 10 + i / 6 * 50);
        }

        ProjectFile.Save(editor.Document, projectPath);

        // A Button in the middle of the screen, surrounded by other controls.
        var target = editor.Screen.Controls[31];
        Assert.Equal(Core.ControlType.Button, target.Type);

        using var session = EditorSession.Launch(recoveryDirectory, projectPath);
        EditorSession.WaitUntil(() => session.Window.Title.StartsWith("quality-", StringComparison.Ordinal), () => "Project did not open.");

        var select = Stopwatch.StartNew();
        session.ClickCanvas(target.X + 50, target.Y + 15);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == target.Name, () => "Could not select a control.");
        select.Stop();

        var move = Stopwatch.StartNew();
        session.DragOnCanvas(target.X + 50, target.Y + 15, target.X + 70, target.Y + 15);
        EditorSession.WaitUntil(() => session.Field("XBox").Text == (target.X + 20).ToString(), () => "Move did not apply.");
        move.Stop();

        // Drag the bottom-right corner handle. (Middle-of-edge handles are hidden on controls
        // shorter than 40 DIPs.)
        var resize = Stopwatch.StartNew();
        var right = target.X + 20 + target.Width;
        var bottom = target.Y + target.Height;
        session.DragOnCanvas(right, bottom, right + 20, bottom);
        EditorSession.WaitUntil(() => session.Field("WidthBox").Text == (target.Width + 20).ToString(), () => "Resize did not apply.");
        resize.Stop();

        var place = Stopwatch.StartNew();
        EditorSession.Within(session.ById("ToolboxList"), session.Find.ByName("Button"), "toolbox Button").Click();
        // Blank space below the last row that is still visible on a 1024 × 768 screen.
        session.ClickCanvas(20, 520);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text.StartsWith("Button", StringComparison.Ordinal)
            && session.Field("XBox").Text == "20" && session.Field("YBox").Text == "520", () => "Placement did not apply.");
        place.Stop();

        session.Screenshot("05-sixty-controls");
        File.AppendAllText(Path.Combine(EditorSession.ArtifactsDirectory, "timings.txt"),
            $"61 controls: select {select.ElapsedMilliseconds} ms, move {move.ElapsedMilliseconds} ms, "
            + $"resize {resize.ElapsedMilliseconds} ms, place {place.ElapsedMilliseconds} ms (each includes simulated pointer movement){Environment.NewLine}");

        // Each gesture includes about a second of simulated pointer movement and waits; far
        // more than that would mean the editor is struggling.
        Assert.True(move.ElapsedMilliseconds < 5000, $"Move took {move.ElapsedMilliseconds} ms.");
        Assert.True(resize.ElapsedMilliseconds < 5000, $"Resize took {resize.ElapsedMilliseconds} ms.");
        Assert.True(place.ElapsedMilliseconds < 5000, $"Placement took {place.ElapsedMilliseconds} ms.");

        session.CloseWindow();
        session.DialogButton("Discard").Invoke();
        session.WaitForExit();
    }

    /// <summary>Tab reaches the toolbox and inspector, and a control can be added with the keyboard.</summary>
    [UiWalkthroughFact]
    public void KeyboardReachesToolboxAndInspector()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        _ = session.Window;

        // Tab from the start of the window until the toolbox has focus.
        Assert.True(TabUntil(session, id => id.StartsWith("ListBoxItem", StringComparison.Ordinal) || id == "ToolboxList", out var reached),
            $"Tab did not reach the toolbox (last focus: {reached}).");

        // Choose Button with the arrow keys and add it with Enter.
        EditorSession.Press(VirtualKeyShort.DOWN);
        EditorSession.Press(VirtualKeyShort.RETURN);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text.Length > 0, () => "Enter in the toolbox did not add a control.");

        // Tab onward into the inspector and rename the new control.
        Assert.True(TabUntil(session, id => id == "NameBox", out reached), $"Tab did not reach the Name field (last focus: {reached}).");
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Type("KeyboardButton");
        EditorSession.Press(VirtualKeyShort.RETURN);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "KeyboardButton", () => "Rename by keyboard failed.");

        // Delete in a text field edits the text; it must not delete the control.
        EditorSession.Press(VirtualKeyShort.HOME);
        EditorSession.Press(VirtualKeyShort.DELETE);
        Assert.Equal("eyboardButton", session.Field("NameBox").Text);
        EditorSession.Press(VirtualKeyShort.ESCAPE);

        session.CloseWindow();
        session.DialogButton("Discard").Invoke();
        session.WaitForExit();
    }

    private static bool TabUntil(EditorSession session, Func<string, bool> isTarget, out string lastFocus)
    {
        lastFocus = "";
        for (var i = 0; i < 40; i++)
        {
            EditorSession.Press(VirtualKeyShort.TAB);
            lastFocus = session.FocusedAutomationId();
            if (isTarget(lastFocus))
            {
                return true;
            }
        }

        return false;
    }
}
