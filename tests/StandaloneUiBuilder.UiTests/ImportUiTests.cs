using FlaUI.Core.WindowsAPI;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.UiTests;

/// <summary>File > Import from WPF, driven through the real editor and file dialog.</summary>
public sealed class ImportUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;
    private readonly string exportDirectory = Directory.CreateTempSubdirectory("uib-import-").FullName;

    public void Dispose()
    {
        Directory.Delete(recoveryDirectory, recursive: true);
        Directory.Delete(exportDirectory, recursive: true);
    }

    [UiWalkthroughFact]
    public void ExportedWindowsImportAsScreens()
    {
        var sample = ProjectFile.Load(Path.Combine(EditorSession.RepositoryRoot, "docs", "samples", "layout-demo.uibproj"));
        var folder = WpfExporter.Export(sample, exportDirectory).ProjectFolder;

        using var session = EditorSession.Launch(recoveryDirectory);
        session.Window.Focus();
        EditorSession.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_F);
        EditorSession.Press(VirtualKeyShort.KEY_I);

        // The Open dialog: go to the folder, then choose both windows.
        var dialog = session.Dialog();
        var fileName = EditorSession.Within(dialog, session.Find.ByAutomationId("1148"), "file name box");
        fileName.Click();
        FlaUI.Core.Input.Keyboard.Type(folder);
        EditorSession.Press(VirtualKeyShort.RETURN);
        Thread.Sleep(800);
        fileName.Click();
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Type("\"MainWindow.xaml\" \"SettingsWindow.xaml\"");
        EditorSession.Press(VirtualKeyShort.RETURN);

        EditorSession.WaitUntil(() => session.Status == "Imported 2 screens from WPF. Save to keep them.", () => $"Status: {session.Status}");
        Assert.Equal($"Layout demo ● — {EditorSession.AppTitle}", session.Window.Title);
        var tabs = session.ById("ScreenTabs");
        EditorSession.Within(tabs, session.Find.ByName("Settings"), "the imported Settings screen");

        // The imported design is the one that was exported: the main screen's OK button is there.
        session.ClickCanvas(70, 550);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "OkButton", () => $"Selected {session.Field("NameBox").Text}.");
        session.Screenshot("15-imported-from-wpf");
    }
}
