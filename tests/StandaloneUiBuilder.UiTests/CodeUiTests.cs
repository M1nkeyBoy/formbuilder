using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Screen > Code through the real editor.</summary>
public sealed class CodeUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void TheCodeWindowShowsTheCodeAndAddsAHook()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj");
        using var session = EditorSession.Launch(recoveryDirectory, sample);
        _ = session.Window;
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.NEXT);
        EditorSession.WaitUntil(() => session.Field("ScreenNameBox").Text == "Settings", () => $"Screen: {session.Field("ScreenNameBox").Text}");

        EditorSession.Press(VirtualKeyShort.F7);
        var window = session.Dialog();
        var code = EditorSession.WaitFor(() => window.FindFirstDescendant(session.Find.ByAutomationId("CodeBox")), "the code box").AsTextBox();
        Assert.Contains("partial void OnSave() => Server = \"saved\";", code.Text);

        // The hooks list ticks OnSave, which the code implements; OnLevelChanged is added by double-clicking it.
        var hooks = EditorSession.WaitFor(() => window.FindFirstDescendant(session.Find.ByAutomationId("HooksList")), "the hooks list");
        Assert.NotNull(hooks.FindFirstDescendant(session.Find.ByName("✓ OnSave()")));
        EditorSession.Within(hooks, session.Find.ByName("OnLevelChanged()"), "OnLevelChanged").DoubleClick();
        EditorSession.WaitUntil(() => code.Text.Contains("partial void OnLevelChanged()", StringComparison.Ordinal), () => $"Code: {code.Text}");
        session.Screenshot("35-code-window");

        // The compiler checks the code as it is typed, and suggests names: the caret is in OnLevelChanged.
        var summary = EditorSession.WaitFor(() => window.FindFirstDescendant(session.Find.ByAutomationId("ProblemsSummary")), "the problems summary");
        EditorSession.WaitUntil(() => summary.Name == "No problems", () => $"Problems: {summary.Name}", TimeSpan.FromMinutes(1));
        Keyboard.Type("Serv");
        var suggestion = EditorSession.WaitFor(
            () => session.TopLevelWindows.Select(w => w.FindFirstDescendant(session.Find.ByName("Server").And(session.Find.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))))
                .FirstOrDefault(found => found is not null),
            "the Server suggestion");
        Assert.False(suggestion.IsOffscreen, "The Server suggestion is not on screen.");
        Thread.Sleep(1000);
        session.Screenshot("42-code-completion");
        EditorSession.ScreenshotScreen("42-code-completion-screen");
        Keyboard.Type(VirtualKeyShort.TAB);
        Keyboard.Type(" = Level.ToString();");
        EditorSession.WaitUntil(() => code.Text.Contains("Server = Level.ToString();", StringComparison.Ordinal), () => $"Code: {code.Text}");
        EditorSession.WaitUntil(() => summary.Name == "No problems", () => $"Problems: {summary.Name}");

        // A name that no suggestion starts with is left as typed.
        Keyboard.Type(" Sever = \"x\";");
        EditorSession.WaitUntil(() => summary.Name == "1 error", () => $"Problems: {summary.Name}", TimeSpan.FromSeconds(30));
        var problems = EditorSession.WaitFor(() => window.FindFirstDescendant(session.Find.ByAutomationId("ProblemsList")), "the problems list");
        Assert.Contains("'Sever'", EditorSession.WaitFor(() => problems.FindFirstDescendant(session.Find.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)), "a problem").Name);
        session.Screenshot("42-code-problem");

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.ENTER);
        EditorSession.WaitUntil(() => session.Status == "Code of Settings changed", () => $"Status: {session.Status}");
    }
}
