using FlaUI.Core.AutomationElements;

namespace StandaloneUiBuilder.UiTests;

/// <summary>GroupBox, RadioButton and Slider through the real editor.</summary>
public sealed class MoreControlsUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void GroupBoxRadioButtonsAndSliderRange()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");
        void Place(string type, int x, int y)
        {
            EditorSession.Within(toolbox, session.Find.ByName(type), "toolbox " + type).Click();
            session.ClickCanvas(x, y);
        }

        // A group box at (100, 100); its children start inside the frame, 20 DIPs down.
        Place("GroupBox", 100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "GroupBox1", () => "The group box was not placed.");
        Place("RadioButton", 150, 130);
        EditorSession.WaitUntil(() => session.Status == "Added RadioButton1 to GroupBox1", () => $"Status: {session.Status}");
        Place("RadioButton", 150, 170);
        EditorSession.WaitUntil(() => session.Status == "Added RadioButton2 to GroupBox1", () => $"Status: {session.Status}");

        // Choosing one radio button clears the other.
        session.ClickCanvas(150, 130);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "RadioButton1", () => "The first radio button is not at the top of the group.");
        session.ById("IsCheckedBox").Click();
        session.ClickCanvas(150, 156);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "RadioButton2", () => "The second radio button is not below the first.");
        session.ById("IsCheckedBox").Click();
        session.ClickCanvas(150, 130);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "RadioButton1", () => "Could not reselect the first radio button.");
        Assert.Equal(FlaUI.Core.Definitions.ToggleState.Off, session.ById("IsCheckedBox").AsCheckBox().ToggleState);

        // A slider's range is checked as a whole: the value must lie within it.
        Place("Slider", 400, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Slider1", () => "The slider was not placed.");
        session.TypeInto("MaximumBox", "10");
        EditorSession.WaitUntil(() => session.ById("InspectorErrorText").Name.Contains("Value must be between 0 and 10", StringComparison.Ordinal),
            () => $"Error: {session.ById("InspectorErrorText").Name}");
        session.TypeInto("ValueBox", "7");
        // Fixing the value clears the error, which applied to the three fields together.
        EditorSession.WaitUntil(() => session.Window.FindFirstDescendant(session.Find.ByAutomationId("InspectorErrorText")) is null,
            () => "The range error is still shown.");
        Assert.Equal(("0", "10", "7"), (session.Field("MinimumBox").Text, session.Field("MaximumBox").Text, session.Field("ValueBox").Text));
        session.Screenshot("13-more-controls");
    }
}
