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

        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.ENTER);
        EditorSession.WaitUntil(() => session.Status == "Code of Settings changed", () => $"Status: {session.Status}");
    }
}
