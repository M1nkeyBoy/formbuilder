using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Format > Align through the real editor.</summary>
public sealed class ArrangeUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void AlignLeftsFollowsTheControlSelectedLast()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var toolbox = session.ById("ToolboxList");
        foreach (var (x, y) in new[] { (100, 100), (300, 200) })
        {
            EditorSession.Within(toolbox, session.Find.ByName("Button"), "toolbox Button").Click();
            session.ClickCanvas(x, y);
        }

        // Select Button1, then Ctrl+click Button2: Button2 is the reference.
        session.ClickCanvas(150, 115);
        Keyboard.Press(VirtualKeyShort.CONTROL);
        session.ClickCanvas(350, 215);
        Keyboard.Release(VirtualKeyShort.CONTROL);
        EditorSession.WaitUntil(() => session.Status == "2 controls selected", () => $"Status: {session.Status}");

        // Format > Align Lefts.
        EditorSession.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_O);
        EditorSession.Press(VirtualKeyShort.KEY_L);
        EditorSession.WaitUntil(() => session.Status == "Aligned lefts with Button2", () => $"Status: {session.Status}");

        // Button1 now starts at Button2's left edge, 300.
        session.ClickCanvas(350, 115);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "Button1" && session.Field("XBox").Text == "300",
            () => $"Selected {session.Field("NameBox").Text} at X {session.Field("XBox").Text}.");
    }
}
