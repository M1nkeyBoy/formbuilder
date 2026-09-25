using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Wpf;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace StandaloneUiBuilder.UiTests;

/// <summary>File > Export to WPF, driven through the real editor and folder picker.</summary>
public sealed class ExportTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;
    private readonly string exportDirectory = Directory.CreateTempSubdirectory("uib-export-").FullName;

    public void Dispose()
    {
        Directory.Delete(recoveryDirectory, recursive: true);
        Directory.Delete(exportDirectory, recursive: true);
    }

    [UiWalkthroughFact]
    public void ExportSampleFromTheEditor()
    {
        var sample = Path.Combine(EditorSession.RepositoryRoot, "docs", "samples", "customer-form.uibproj");
        using var session = EditorSession.Launch(recoveryDirectory, sample);
        EditorSession.WaitUntil(() => session.Window.Title.StartsWith("customer-form", StringComparison.Ordinal), () => "Sample did not open.");

        session.Window.Focus();
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_E);

        // The Windows folder picker: type the folder, then choose Select Folder.
        var picker = session.Dialog();
        var folderBox = EditorSession.WaitFor(
            () => picker.FindFirstDescendant(session.Find.ByControlType(UiaControlType.Edit).And(session.Find.ByName("Folder:")))
                ?? picker.FindFirstDescendant(session.Find.ByAutomationId("1152")),
            "folder box");
        folderBox.Click();
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Type(exportDirectory);

        // Select Folder may first navigate into the typed folder; press it until the picker closes.
        for (var attempt = 0; attempt < 3 && session.Window.ModalWindows.Any(w => w.Title != EditorSession.AppTitle); attempt++)
        {
            picker.FindFirstDescendant(session.Find.ByAutomationId("1").And(session.Find.ByControlType(UiaControlType.Button)))?.AsButton().Invoke();
            Thread.Sleep(800);
        }

        // The summary asks whether to open the folder.
        session.DialogButton("No").Invoke();

        var folder = Path.Combine(exportDirectory, "CustomerForm");
        EditorSession.WaitUntil(() => File.Exists(Path.Combine(folder, "MainWindow.xaml")), () => $"Nothing was exported to {folder}.");
        EditorSession.WaitUntil(() => session.Status.StartsWith("Exported WPF project", StringComparison.Ordinal), () => $"Status: {session.Status}");

        var sampleDocument = ProjectFile.Load(sample);
        Assert.Equal(
            WpfGenerator.WindowXaml(sampleDocument with { Name = "customer-form" }, "CustomerForm"),
            File.ReadAllText(Path.Combine(folder, "MainWindow.xaml")));
        Assert.True(File.Exists(Path.Combine(folder, "CustomerForm.csproj")));

        // Exporting does not change the design, so closing needs no prompt.
        session.CloseWindow();
        session.WaitForExit();
    }
}
