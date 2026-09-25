using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using StandaloneUiBuilder.Core;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// The prototype acceptance walkthrough from the spec, driven through the real UI:
/// Launch → New → place all five controls → move and resize → change properties →
/// undo/redo → Preview interactions → Design → Save As → close → reopen → edit → Save,
/// followed by the unsaved-changes prompt and crash recovery.
/// </summary>
public sealed class WalkthroughTests : IDisposable
{
    private readonly string projectPath = Path.Combine(
        EditorSession.ArtifactsDirectory, $"walkthrough-{Guid.NewGuid():N}.uibproj");

    public void Dispose()
    {
        File.Delete(projectPath);
    }

    [UiWalkthroughFact]
    public void FullWalkthrough()
    {
        using (var editor = EditorSession.Launch())
        {
            Assert.Equal($"Untitled — {EditorSession.AppTitle}", editor.Window.Title);
            PlaceAllFiveControls(editor);
            MoveAndResize(editor);
            EditProperties(editor);
            UndoAndRedo(editor);
            PreviewDoesNotChangeTheDesign(editor);
            SaveAs(editor);
            UnsavedChangesPromptCanBeCancelled(editor);
        }

        AssertSavedProject(expectedX: 150);

        using (var editor = EditorSession.Launch(projectPath))
        {
            ReopenEditAndSave(editor);
        }

        AssertSavedProject(expectedX: 170);

        CrashRecovery();
    }

    private static void PlaceAllFiveControls(EditorSession editor)
    {
        // Drag and drop from the toolbox.
        var toolbox = editor.ById("ToolboxList");
        var buttonItem = EditorSession.Within(toolbox, editor.Find.ByName("Button"), "toolbox Button");
        EditorSession.Drag(buttonItem.GetClickablePoint(), editor.Canvas(100, 100));
        EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "Button1", () => $"Button1 was not placed. Status: {editor.Status}");
        Assert.Equal("100", editor.Field("XBox").Text);
        Assert.Equal("100", editor.Field("YBox").Text);

        // Select a toolbox item, then click the canvas.
        foreach (var (type, x, y) in new[] { ("Label", 40, 200), ("TextBox", 200, 200), ("CheckBox", 40, 260), ("ComboBox", 200, 260) })
        {
            EditorSession.Within(toolbox, editor.Find.ByName(type), "toolbox " + type).Click();
            editor.ClickCanvas(x, y);
            EditorSession.WaitUntil(() => editor.Field("NameBox").Text == type + "1", () => $"{type}1 was not placed. Status: {editor.Status}");
        }

        Assert.Contains("●", editor.Window.Title);
        editor.Screenshot("01-placed");
    }

    private static void MoveAndResize(EditorSession editor)
    {
        editor.ClickCanvas(150, 115);
        EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "Button1", () => "Clicking Button1 did not select it.");

        editor.DragOnCanvas(150, 115, 200, 155);
        EditorSession.WaitUntil(() => editor.Field("XBox").Text == "150" && editor.Field("YBox").Text == "140",
            () => $"Move ended at {editor.Field("XBox").Text}, {editor.Field("YBox").Text}.");

        // Bottom-right resize handle of a 100 × 30 control at 150, 140.
        editor.DragOnCanvas(250, 170, 290, 190);
        EditorSession.WaitUntil(() => editor.Field("WidthBox").Text == "140" && editor.Field("HeightBox").Text == "50",
            () => $"Resize ended at {editor.Field("WidthBox").Text} × {editor.Field("HeightBox").Text}.");
    }

    private static void EditProperties(EditorSession editor)
    {
        editor.TypeInto("NameBox", "SubmitButton");
        editor.TypeInto("TextValueBox", "Send");
        EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "SubmitButton", () => "Rename was not applied.");

        // Invalid input is flagged and not applied; Esc restores the stored value.
        editor.TypeInto("WidthBox", "5");
        var error = editor.ById("InspectorErrorText");
        EditorSession.WaitUntil(() => error.Name.Contains("at least 30"), () => $"No validation message; got \"{error.Name}\".");
        EditorSession.Press(VirtualKeyShort.ESCAPE);
        EditorSession.WaitUntil(() => editor.Field("WidthBox").Text == "140", () => "Esc did not restore the width.");
    }

    private static void UndoAndRedo(EditorSession editor)
    {
        // Put keyboard focus on the canvas so Ctrl+Z reaches the editor, not a text field.
        editor.ClickCanvas(200, 160);

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.WaitUntil(() => editor.Field("TextValueBox").Text == "Button1", () => "Undo did not restore the text.");

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "Button1", () => "Second undo did not restore the name.");

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
        EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "SubmitButton" && editor.Field("TextValueBox").Text == "Send",
            () => "Redo did not reapply the edits.");
        editor.Screenshot("02-edited");
    }

    private static void PreviewDoesNotChangeTheDesign(EditorSession editor)
    {
        editor.ById("PreviewModeButton").Click();
        var surface = editor.ById("SurfaceScroller");

        var textBox = EditorSession.Within(surface, editor.Find.ByControlType(UiaControlType.Edit), "preview TextBox").AsTextBox();
        textBox.Click();
        FlaUI.Core.Input.Keyboard.Type("typed in preview");

        EditorSession.Within(surface, editor.Find.ByControlType(UiaControlType.CheckBox), "preview CheckBox").Click();

        var combo = EditorSession.Within(surface, editor.Find.ByControlType(UiaControlType.ComboBox), "preview ComboBox").AsComboBox();
        combo.Select(1);

        // Close the drop-down first: WPF uses the first click outside an open drop-down to close it.
        combo.Collapse();

        EditorSession.Within(surface, editor.Find.ByName("Send").And(editor.Find.ByControlType(UiaControlType.Button)), "preview Button").Click();
        EditorSession.WaitUntil(() => editor.Status == "SubmitButton clicked", () => $"Button click not acknowledged; status \"{editor.Status}\".");
        editor.Screenshot("03-preview");

        editor.ById("DesignModeButton").Click();
    }

    private void SaveAs(EditorSession editor)
    {
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S);
        var dialog = editor.Dialog();
        // Type the path as a user would; setting the value directly is not always picked up by
        // the Windows file dialog.
        EditorSession.Within(dialog, editor.Find.ByAutomationId("1001"), "file name box").Click();
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Type(projectPath);
        EditorSession.Press(VirtualKeyShort.RETURN);

        var name = Path.GetFileNameWithoutExtension(projectPath);
        EditorSession.WaitUntil(() => editor.Window.Title == $"{name} — {EditorSession.AppTitle}", () => $"Title after save: {editor.Window.Title}");
        Assert.True(File.Exists(projectPath));
    }

    private static void UnsavedChangesPromptCanBeCancelled(EditorSession editor)
    {
        editor.ClickCanvas(200, 160);
        EditorSession.Press(VirtualKeyShort.DELETE);
        EditorSession.WaitUntil(() => editor.Window.Title.Contains('●'), () => "Delete did not mark the project changed.");

        // Cancel keeps the work open.
        editor.CloseWindow();
        editor.DialogButton("Cancel").Invoke();
        EditorSession.WaitUntil(() => !editor.App.HasExited && editor.Window.ModalWindows.Length == 0, () => "Cancel did not keep the editor open.");

        // Undoing back to the saved state is clean again, so closing needs no prompt.
        editor.ClickCanvas(600, 500);
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.WaitUntil(() => !editor.Window.Title.Contains('●'), () => "Undo back to the saved state still shows unsaved changes.");

        editor.CloseWindow();
        editor.WaitForExit();
    }

    private static void ReopenEditAndSave(EditorSession editor)
    {
        EditorSession.WaitUntil(() => editor.Window.Title.StartsWith("walkthrough-", StringComparison.Ordinal),
            () => $"Reopened title: {editor.Window.Title}");

        editor.ClickCanvas(200, 160);
        EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "SubmitButton", () => "The reopened button could not be selected.");
        editor.DragOnCanvas(200, 160, 220, 160);
        EditorSession.WaitUntil(() => editor.Field("XBox").Text == "170", () => $"Move after reopen ended at {editor.Field("XBox").Text}.");

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        EditorSession.WaitUntil(() => !editor.Window.Title.Contains('●'), () => "Ctrl+S did not save.");

        editor.CloseWindow();
        editor.WaitForExit();
    }

    private void CrashRecovery()
    {
        using (var editor = EditorSession.Launch(projectPath))
        {
            editor.ClickCanvas(220, 160);
            EditorSession.WaitUntil(() => editor.Field("NameBox").Text == "SubmitButton", () => "Could not select the button before the crash.");
            editor.DragOnCanvas(220, 160, 320, 160);
            EditorSession.WaitUntil(() => editor.Field("XBox").Text == "270", () => "Move before the crash failed.");

            // Give the idle timer time to write the recovery draft, then end the process abruptly.
            Thread.Sleep(3500);
            editor.App.Kill();
        }

        using (var editor = EditorSession.Launch(projectPath))
        {
            editor.DialogButton("Yes").Invoke();
            EditorSession.WaitUntil(() => editor.Window.Title.Contains('●'), () => "Recovered work is not shown as unsaved.");
            editor.ClickCanvas(320, 160);
            EditorSession.WaitUntil(() => editor.Field("XBox").Text == "270", () => "The recovered draft does not contain the moved button.");
            editor.Screenshot("04-recovered");

            // Declining to save keeps the explicit save on disk untouched.
            editor.CloseWindow();
            editor.DialogButton("Discard").Invoke();
            editor.WaitForExit();
        }

        AssertSavedProject(expectedX: 170);
    }

    private void AssertSavedProject(int expectedX)
    {
        var document = ProjectFile.Load(projectPath);
        var controls = document.Screen.Controls;

        Assert.Equal(
            [Core.ControlType.Button, Core.ControlType.Label, Core.ControlType.TextBox, Core.ControlType.CheckBox, Core.ControlType.ComboBox],
            controls.Select(c => c.Type));

        var button = controls[0];
        Assert.Equal("SubmitButton", button.Name);
        Assert.Equal("Send", button.Properties.Text);
        Assert.Equal(new ControlBounds(expectedX, 140, 140, 50), button.Bounds);

        // Nothing done in Preview reached the saved design.
        Assert.Equal("", controls[2].Properties.Text);
        Assert.False(controls[3].Properties.IsChecked);
    }
}
