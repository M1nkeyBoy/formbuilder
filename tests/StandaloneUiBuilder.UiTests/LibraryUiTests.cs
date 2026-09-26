using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Libraries;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// Control libraries with a real package from nuget.org: the Extended WPF Toolkit, free and
/// small. Added through the editor, placed and set in the inspector, then exported, built and
/// run as a WPF application.
/// </summary>
public sealed class LibraryUiTests : IDisposable
{
    private const string Package = "Extended.Wpf.Toolkit";
    private const string Version = "4.6.1";

    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void ALibraryIsAddedAndItsControlsPlacedAndSet()
    {
        using var session = EditorSession.Launch(recoveryDirectory, askPlatform: true);
        var picker = session.Dialog();
        var platforms = EditorSession.WaitFor(() => picker.FindFirstDescendant(session.Find.ByAutomationId("PlatformList")), "the platform list");
        EditorSession.WaitFor(() => platforms.FindFirstDescendant(session.Find.ByName("WPF")), "WPF").AsListBoxItem().Select();
        EditorSession.WaitFor(() => picker.FindFirstDescendant(session.Find.ByAutomationId("CreateButton")), "Create").AsButton().Invoke();
        EditorSession.WaitUntil(() => session.ById("PlatformButton").Name == "WPF", () => $"Platform: {session.ById("PlatformButton").Name}");

        // Project > Libraries, from the keyboard.
        session.Window.Focus();
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_L);
        var libraries = session.Dialog();
        Assert.Equal("Libraries", libraries.Title);
        TypeIn(libraries, session, "PackageBox", Package);
        TypeIn(libraries, session, "VersionBox", Version);
        EditorSession.WaitFor(() => libraries.FindFirstDescendant(session.Find.ByAutomationId("AddButton")), "Add").AsButton().Invoke();
        var status = EditorSession.WaitFor(() => libraries.FindFirstDescendant(session.Find.ByAutomationId("LibraryStatusText")), "the status");
        EditorSession.WaitUntil(() => status.Name.StartsWith($"Added {Package} {Version}:", StringComparison.Ordinal), () => $"Status: {status.Name}",
            TimeSpan.FromMinutes(5));
        Capture.Element(libraries).ToFile(Path.Combine(EditorSession.ArtifactsDirectory, "39-libraries.png"));
        EditorSession.WaitFor(() => libraries.FindFirstDescendant(session.Find.ByAutomationId("CloseButton")), "Close").AsButton().Invoke();

        // The package's controls are in the toolbox: place an IntegerUpDown.
        session.TypeInto("ControlSearchBox", "IntegerUp");
        EditorSession.Within(session.ById("ToolboxList"), session.Find.ByName("IntegerUpDown"), "the IntegerUpDown tile").Click();
        session.ClickCanvas(100, 100);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "IntegerUpDown1", () => $"Selected {session.Field("NameBox").Text}");
        Assert.Equal("IntegerUpDown · Main", session.ById("TypeText").Name);

        // The preview host draws it as it looks; until then it is a labelled box.
        var surface = session.ById("Surface");
        bool Drawn() => surface.FindFirstDescendant(session.Find.ByAutomationId("IntegerUpDown1").And(session.Find.ByControlType(FlaUI.Core.Definitions.ControlType.Image))) is not null;
        EditorSession.WaitUntil(Drawn, () => "The IntegerUpDown was not drawn.", TimeSpan.FromMinutes(1));

        // Its properties are in the inspector's Library section.
        session.TypeInto("LibraryFormatString", "N0");
        EditorSession.WaitUntil(() => session.Status.Length > 0 && session.Field("LibraryFormatString").Text == "N0", () => $"Format: {session.Field("LibraryFormatString").Text}");
        session.TypeInto("LibraryMaxLength", "lots");
        EditorSession.WaitUntil(() => session.ById("InspectorErrorText").Name.Contains("MaxLength must be a whole number", StringComparison.Ordinal),
            () => $"Message: {session.ById("InspectorErrorText").Name}");
        session.TypeInto("LibraryMaxLength", "6");

        // Drawn again with its new values.
        EditorSession.WaitUntil(Drawn, () => "The IntegerUpDown was not drawn again.", TimeSpan.FromMinutes(1));
        session.Screenshot("39-library-control");

        // Undo takes the value back off; the project remembers the library.
        EditorSession.Press(VirtualKeyShort.ESCAPE);
        session.ClickCanvas(400, 400);
        EditorSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        session.ClickCanvas(110, 110);
        EditorSession.WaitUntil(() => session.Field("NameBox").Text == "IntegerUpDown1" && session.Field("LibraryMaxLength").Text == "",
            () => $"MaxLength: {session.Field("LibraryMaxLength").Text}");
    }

    /// <summary>The whole way: the package scanned, a design exported to WPF, built with the package, and run.</summary>
    [UiWalkthroughFact]
    public void AnExportedLibraryControlBuildsAndRuns()
    {
        var cache = new PackageCache(new FlatContainerFeed(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) }), Path.Combine(Path.GetTempPath(), "uib-packages"));
        var package = LibraryLoader.LoadAsync(cache, Package, Version, ProjectPlatform.Wpf).GetAwaiter().GetResult();
        Assert.Contains(package.Controls, c => c.TypeName == "Xceed.Wpf.Toolkit.IntegerUpDown");

        var editor = new DesignEditor();
        editor.New(ProjectPlatform.Wpf);
        Assert.Null(editor.AddLibrary(package));
        var upDown = editor.AddLibraryControl("Xceed.Wpf.Toolkit.IntegerUpDown", 20, 20)!;
        Assert.Null(editor.SetLibrarySetting(upDown.Id, "FormatString", "N0"));
        Assert.Null(editor.SetLibrarySetting(upDown.Id, "AllowSpin", "True"));
        var picker = editor.AddLibraryControl("Xceed.Wpf.Toolkit.ColorPicker", 20, 80)!;
        Assert.Null(editor.SetLibrarySetting(picker.Id, "DisplayColorAndName", "True"));
        editor.AddControl(ControlType.Button, 20, 140);

        var parent = Environment.GetEnvironmentVariable("UIB_EXPORT_DIR") is { Length: > 0 } exports
            ? Path.Combine(exports, "wpf-library")
            : Directory.CreateTempSubdirectory("uib-library-").FullName;
        var folder = WpfExporter.Export(editor.Document with { Name = "LibraryDemo" }, parent).ProjectFolder;
        var exe = Build(folder, "LibraryDemo");

        using var automation = new FlaUI.UIA3.UIA3Automation();
        using var app = FlaUI.Core.Application.Launch(exe);
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(60)) ?? throw new InvalidOperationException("The window did not open.");
            // The toolkit's controls show UI Automation their parts rather than themselves: the
            // IntegerUpDown its text box, beside the builder's own button.
            EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Button1")), "the button");
            EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)), "the IntegerUpDown's text box");
            Thread.Sleep(500);
            Capture.Element(window).ToFile(Path.Combine(EditorSession.ArtifactsDirectory, "39-wpf-library-app.png"));
        }
        finally
        {
            app.Close();
        }
    }

    private static void TypeIn(Window dialog, EditorSession session, string automationId, string text)
    {
        var box = EditorSession.WaitFor(() => dialog.FindFirstDescendant(session.Find.ByAutomationId(automationId)), automationId).AsTextBox();
        box.Focus();
        box.Text = text;
    }

    private static string Build(string folder, string projectName)
    {
        var build = Process.Start(new ProcessStartInfo("dotnet", ["build", folder, "--configuration", "Release", "--nologo"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
        build.WaitForExit();
        Assert.True(build.ExitCode == 0, "The exported WPF project did not build:\n" + output);
        return Path.Combine(folder, "bin", "Release", "net10.0-windows", projectName + ".exe");
    }
}
