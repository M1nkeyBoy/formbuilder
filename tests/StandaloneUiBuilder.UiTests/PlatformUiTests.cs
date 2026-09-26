using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Choosing a new project's platform, and changing it, through the real editor.</summary>
public sealed class PlatformUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void ANewProjectAsksForItsPlatformAndExportsOnlyToIt()
    {
        using var session = EditorSession.Launch(recoveryDirectory, askPlatform: true);
        var dialog = session.Dialog();
        Assert.Equal("New project", dialog.Title);
        session.Screenshot("38-new-project");

        var platforms = EditorSession.WaitFor(() => dialog.FindFirstDescendant(session.Find.ByAutomationId("PlatformList")), "the platform list");
        EditorSession.WaitFor(() => platforms.FindFirstDescendant(session.Find.ByName("Blazor")), "Blazor").AsListBoxItem().Select();
        EditorSession.WaitFor(() => dialog.FindFirstDescendant(session.Find.ByAutomationId("CreateButton")), "Create").AsButton().Invoke();

        EditorSession.WaitUntil(() => session.ById("PlatformButton").Name == "Blazor", () => $"Platform: {session.ById("PlatformButton").Name}");
        Assert.Equal("Export to Blazor", session.ById("ExportButton").Name);

        // The header's platform menu changes it back to any platform, and Export offers every target again.
        session.ById("PlatformButton").Click();
        session.PopupMenuItem("Any platform").Click();
        Wait.UntilInputIsProcessed();
        EditorSession.WaitUntil(() => session.ById("PlatformButton").Name == "Any platform", () => $"Platform: {session.ById("PlatformButton").Name}");
        Assert.Equal("Export ▾", session.ById("ExportButton").Name);
        Assert.Equal("Platform: any; the project exports to all five", session.Status);
    }
}
