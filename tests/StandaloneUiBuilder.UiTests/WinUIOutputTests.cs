using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinUI;

namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// Builds the WinUI 3 export of the layout demo, runs it and checks where its controls really
/// are through UI Automation, on both screens: the second opened by the OK button's action and
/// closed by its Close button.
/// </summary>
public sealed class WinUIOutputTests
{
    private static ProjectDocument Sample(string name) =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj"));

    /// <summary>Exports the samples, so CI can also build and run them.</summary>
    [WindowsFact]
    public void SampleExports()
    {
        var parent = Path.Combine(Environment.GetEnvironmentVariable("UIB_EXPORT_DIR") ?? Directory.CreateTempSubdirectory("uib-export-").FullName, "winui");
        WinUIExporter.Export(Sample("customer-form"), parent);
        var result = WinUIExporter.Export(Sample("layout-demo"), parent);

        Assert.True(File.Exists(Path.Combine(result.ProjectFolder, "MainWindow.xaml")));
    }

    [UiWalkthroughFact]
    public void GeneratedWindowsLayOutEveryScreen()
    {
        var document = Sample("layout-demo");
        var folder = WinUIExporter.Export(document, Directory.CreateTempSubdirectory("uib-winui-").FullName).ProjectFolder;
        var exe = Build(folder, "LayoutDemo");

        using var automation = new FlaUI.UIA3.UIA3Automation();
        using var app = FlaUI.Core.Application.Launch(exe);
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(60))
                ?? throw new InvalidOperationException("The generated window did not open.");
            window.Patterns.Transform.Pattern.Move(0, 0);
            Thread.Sleep(500);
            AssertLayout(window, document.MainScreen);
            Screenshot(window, "40-winui-main");

            // OK opens Settings in a window of its own; Close closes it.
            EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("OkButton")), "OK button").Click();
            var settings = document.Screens[1];
            var second = EditorSession.WaitFor(() => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == settings.Name), "the Settings window");
            second.Patterns.Transform.Pattern.Move(0, 0);
            Thread.Sleep(500);
            AssertLayout(second, settings);
            Screenshot(second, "41-winui-settings");

            // The drawn TabControl's tabs have no automation ID of their own.
            TabOrderCheck.AssertTabOrder(automation, second, settings, c => c.Type == Core.ControlType.TabControl);

            EditorSession.WaitFor(() => second.FindFirstDescendant(cf => cf.ByAutomationId("CloseSettingsButton")), "Close button").Click();
            EditorSession.WaitUntil(() => app.GetAllTopLevelWindows(automation).All(w => w.Title != settings.Name), () => "The Settings window did not close.");
        }
        finally
        {
            app.Close();
            if (!app.HasExited)
            {
                app.Kill();
            }
        }
    }

    /// <summary>
    /// Compares each control with the Core layout at the design size. Panels and labels have no
    /// UI Automation element in WinUI, so the controls inside them stand for them.
    /// </summary>
    private static void AssertLayout(Window window, ScreenDocument screen)
    {
        var client = WinFormsOutputTests.ClientRect(window);
        Assert.True(Math.Abs(client.Width - screen.Width) <= 1 && Math.Abs(client.Height - screen.Height) <= 1,
            $"{screen.Name}: the window's client area is {client.Width} × {client.Height}, expected {screen.Width} × {screen.Height}.");

        var checkedCount = 0;
        foreach (var placed in ContainerLayout.Flatten(screen))
        {
            var control = placed.Control;
            if (placed.IsHidden || ControlCatalog.Get(control.Type).IsContainer || control.Type == Core.ControlType.Label)
            {
                continue;
            }

            var element = EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId(control.Name)), $"{control.Name} in the {screen.Name} window");
            var r = element.BoundingRectangle;
            var actual = new ControlBounds(r.Left - client.Left, r.Top - client.Top, r.Width, r.Height);

            // WinUI reports a ComboBox's UI Automation box 4 pixels outside the control on every
            // side (checked against the screenshot); the control itself has its designed box.
            if (control.Type == Core.ControlType.ComboBox)
            {
                actual = new ControlBounds(actual.X + 4, actual.Y + 4, actual.Width - 8, actual.Height - 8);
            }
            var expected = PictureFit.Expected(control, placed.Bounds);

            // Shared grid rows and columns are rounded to whole pixels; allow one per track.
            var tolerance = 1;
            for (var parentId = placed.ParentId; parentId is { } id;)
            {
                var parent = ControlTree.Find(screen.Controls, id)!;
                if (parent.Type == Core.ControlType.Grid)
                {
                    tolerance = Math.Max(tolerance, Math.Max(parent.Properties.Rows ?? 1, parent.Properties.Columns ?? 1));
                }

                parentId = ControlTree.ParentOf(screen.Controls, id)?.Id;
            }

            bool Near(int a, int b) => Math.Abs(a - b) <= tolerance;
            Assert.True(
                Near(expected.X, actual.X) && Near(expected.Y, actual.Y) && Near(expected.Width, actual.Width) && Near(expected.Height, actual.Height),
                $"{screen.Name}: {control.Name} ({control.Type}, depth {placed.Depth}): window has {actual}, expected {expected}");
            checkedCount++;
        }

        Assert.True(checkedCount > 5, $"Only {checkedCount} controls were checked on {screen.Name}.");
    }

    private static string Build(string folder, string projectName)
    {
        var build = Process.Start(new ProcessStartInfo("dotnet", ["build", folder, "--configuration", "Release", "--nologo"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = build.StandardOutput.ReadToEndAsync();
        var error = build.StandardError.ReadToEndAsync();
        build.WaitForExit();
        Assert.True(build.ExitCode == 0, "The generated WinUI project did not build:\n" + output.Result + error.Result);
        return Directory.GetFiles(Path.Combine(folder, "bin", "Release"), projectName + ".exe", SearchOption.AllDirectories).Single();
    }

    private static void Screenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("UIB_UI_TEST_ARTIFACTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            FlaUI.Core.Capturing.Capture.Element(window).ToFile(Path.Combine(folder, name + ".png"));
        }
    }
}
