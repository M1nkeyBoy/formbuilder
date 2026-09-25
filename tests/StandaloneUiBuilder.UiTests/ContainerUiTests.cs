using FlaUI.Core.AutomationElements;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Building with StackPanel and Grid containers through the real editor.</summary>
public sealed class ContainerUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void StackAndGridHoldAndArrangeControls()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");
        void Place(string type, int x, int y)
        {
            EditorSession.Within(toolbox, session.Find.ByName(type), "toolbox " + type).Click();
            session.ClickCanvas(x, y);
        }

        // A stack at (100, 100), 200 × 150, with a button and a text box dropped into it.
        Place("StackPanel", 100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "StackPanel1", () => "The stack was not placed.");
        Place("Button", 150, 110);
        EditorSession.WaitUntil(() => session.Status == "Added Button1 to StackPanel1", () => $"Status: {session.Status}");
        Place("TextBox", 150, 200);
        EditorSession.WaitUntil(() => session.Status == "Added TextBox1 to StackPanel1", () => $"Status: {session.Status}");

        // Inside a vertical stack a control has only a height; the stack sets the rest.
        Assert.Equal("30", session.Field("HeightBox").Text);
        Assert.Null(session.Window.FindFirstDescendant(session.Find.ByAutomationId("XBox")));

        // Select the stack through its empty area and turn it horizontal.
        session.ClickCanvas(150, 230);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "StackPanel1", () => "The stack could not be selected.");
        var direction = session.ById("OrientationBox").AsComboBox();
        direction.Select("Horizontal");

        // Close the drop-down first: WPF uses the first click outside an open drop-down to close it.
        direction.Collapse();
        EditorSession.WaitUntil(() => session.Window.Title.Contains('●'), () => "The orientation change was not applied.");

        // Now the text box sits to the right of the button (100 + 100 + 6) and has only a width.
        session.ClickCanvas(250, 200);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TextBox1" && session.Field("WidthBox").Text == "120",
            () => $"Selected {session.Field("NameBox").Text}.");
        session.Screenshot("10-horizontal-stack");

        // Drag the text box out of the stack onto the screen.
        session.DragOnCanvas(250, 200, 500, 450);
        EditorSession.WaitUntil(() => session.Status == "Moved TextBox1 onto the screen", () => $"Status: {session.Status}");
        EditorSession.WaitUntil(() => session.Field("XBox").Text == "460" && session.Field("YBox").Text == "350",
            () => $"Dropped at {session.Field("XBox").Text}, {session.Field("YBox").Text}.");

        // A 2 × 2 grid at (100, 350); drag the text box into its bottom-right cell.
        Place("Grid", 100, 350);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Grid1", () => "The grid was not placed.");
        session.DragOnCanvas(470, 360, 300, 470);
        EditorSession.WaitUntil(() => session.Status == "Moved TextBox1 into Grid1", () => $"Status: {session.Status}");
        session.ClickCanvas(280, 470);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TextBox1", () => "The text box is not in the bottom-right cell.");
        Assert.Equal(("1", "1"), (session.Field("RowBox").Text, session.Field("ColumnBox").Text));

        // Move it to the bottom-left cell from the Properties panel.
        session.TypeInto("ColumnBox", "0");
        session.ClickCanvas(160, 470);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TextBox1", () => "The text box did not move to the bottom-left cell.");

        // Span both columns: now it also covers the bottom-right cell.
        session.TypeInto("ColumnSpanBox", "2");
        session.ClickCanvas(20, 540);
        session.ClickCanvas(300, 470);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TextBox1" && session.Field("ColumnSpanBox").Text == "2",
            () => $"Clicking the bottom-right cell selected {session.Field("NameBox").Text}.");
        session.Screenshot("11-grid");

        // Preview shows real controls inside the containers.
        session.ById("PreviewModeButton").Click();
        var surface = session.ById("SurfaceScroller");
        EditorSession.Within(surface, session.Find.ByName("Button1").And(session.Find.ByControlType(UiaControlType.Button)), "preview button in the stack");
        EditorSession.Within(surface, session.Find.ByControlType(UiaControlType.Edit), "preview text box in the grid");
        session.Screenshot("12-containers-preview");
        session.ById("DesignModeButton").Click();

        session.CloseWindow();
        session.DialogButton("Discard").Invoke();
        session.WaitForExit();
    }
}
