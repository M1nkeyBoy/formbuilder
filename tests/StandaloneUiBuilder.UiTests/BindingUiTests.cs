using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>The Binding field through the real editor.</summary>
public sealed class BindingUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void TheBindingFieldBindsAndExplainsNamesItRejects()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");
        EditorSession.Within(toolbox, session.Find.ByName("TextBox"), "toolbox TextBox").Click();
        session.ClickCanvas(100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "TextBox1", () => $"Selected {session.Field("NameBox").Text}");

        session.TypeInto("BindingBox", "customerName");
        EditorSession.WaitUntil(() => session.ById("InspectorErrorText").Name.Contains("capital letter", StringComparison.Ordinal),
            () => $"Message: {session.ById("InspectorErrorText").Name}");

        session.TypeInto("BindingBox", "CustomerName");
        Assert.Equal("CustomerName", session.Field("BindingBox").Text);
        session.Screenshot("32-binding");

        // Undo, from the canvas, removes the binding (had binding failed, it would remove the text box).
        session.ClickCanvas(110, 110);
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        EditorSession.WaitUntil(() => session.Field("BindingBox").Text == "", () => $"Binding after undo: {session.Field("BindingBox").Text}");
    }
}
