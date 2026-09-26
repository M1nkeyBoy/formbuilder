using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Maui;

namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// Builds the .NET MAUI export of the layout demo for Windows, runs it and checks where its
/// controls are through UI Automation, on both screens: the second opened by the OK button's
/// action and closed by its Close button.
/// </summary>
public sealed class MauiOutputTests
{
    private static ProjectDocument Sample(string name) =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj"));

    /// <summary>Exports the samples, so CI can also build and run them.</summary>
    [WindowsFact]
    public void SampleExports()
    {
        var parent = Path.Combine(Environment.GetEnvironmentVariable("UIB_EXPORT_DIR") ?? Directory.CreateTempSubdirectory("uib-export-").FullName, "maui");
        MauiExporter.Export(Sample("customer-form"), parent);
        var result = MauiExporter.Export(Sample("layout-demo"), parent);

        Assert.True(File.Exists(Path.Combine(result.ProjectFolder, "MainPage.xaml")));
    }

    [UiWalkthroughFact]
    public void GeneratedPagesLayOutEveryScreen()
    {
        var document = Sample("layout-demo");
        var folder = MauiExporter.Export(document, Directory.CreateTempSubdirectory("uib-maui-").FullName).ProjectFolder;
        var exe = Build(folder, "LayoutDemo");

        using var automation = new FlaUI.UIA3.UIA3Automation();
        using var app = FlaUI.Core.Application.Launch(exe);
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(90))
                ?? throw new InvalidOperationException("The generated app did not open.");
            window.Patterns.Transform.Pattern.Move(0, 0);
            Thread.Sleep(1000);
            Screenshot(window, "50-maui-main");
            var main = document.MainScreen;
            AssertLayout(window, main, main.Width, main.Height);

            // OK opens Settings as a modal page in the same window; Close goes back.
            EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("OkButton")), "OK button").Click();
            EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CloseSettingsButton")), "the Settings page");
            Thread.Sleep(1000);
            Screenshot(window, "51-maui-settings");
            // A modal page fills the window, which has the first screen's size, so the Settings
            // screen is laid out at that size: its anchored controls move and stretch.
            AssertLayout(window, document.Screens[1], main.Width, main.Height);

            BindingCheck.AssertSliderMovesProgress(window);

            EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CloseSettingsButton")), "Close button").Click();
            EditorSession.WaitUntil(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CloseSettingsButton")) is null, () => "The Settings page did not close.");
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
    /// Compares controls with the Core layout at the design size. MAUI draws its own title bar
    /// inside the window, so where the page starts is taken from the first control, and every
    /// other control must be in the same place relative to it. Labels, panels and check boxes
    /// (a box beside a label in MAUI) are skipped; the controls around them stand for them.
    /// </summary>
    private static void AssertLayout(Window window, ScreenDocument screen, int width, int height)
    {
        (int X, int Y)? origin = null;
        var report = new List<string>();
        foreach (var placed in ContainerLayout.Flatten(screen, width, height))
        {
            var control = placed.Control;
            if (placed.IsHidden || ControlCatalog.Get(control.Type).IsContainer || control.Type is Core.ControlType.Label or Core.ControlType.CheckBox)
            {
                continue;
            }

            var element = EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId(control.Name)), $"{control.Name} on {screen.Name}");
            var r = element.BoundingRectangle;
            var actual = new ControlBounds(r.Left, r.Top, r.Width, r.Height);

            // As in WinUI, a Picker's automation box is 4 pixels outside the control.
            if (control.Type == Core.ControlType.ComboBox)
            {
                actual = new ControlBounds(actual.X + 4, actual.Y + 4, actual.Width - 8, actual.Height - 8);
            }

            var expected = placed.Bounds;
            origin ??= (actual.X - expected.X, actual.Y - expected.Y);
            var shifted = actual with { X = actual.X - origin.Value.X, Y = actual.Y - origin.Value.Y };
            report.Add($"{control.Name} ({control.Type}): page has {shifted}, expected {expected}");

            // MAUI reports a RadioButton's box as just its circle and text, and lets it keep its
            // own height, so only where it starts is compared.
            if (control.Type == Core.ControlType.RadioButton)
            {
                shifted = shifted with { Width = expected.Width, Height = expected.Height };
            }

            var tolerance = placed.ParentId is not null && ControlTree.Find(screen.Controls, placed.ParentId.Value)?.Type == Core.ControlType.Grid ? 3 : 1;
            bool Near(int a, int b) => Math.Abs(a - b) <= tolerance;
            bool Matches(ControlBounds box) => Near(box.X, shifted.X) && Near(box.Y, shifted.Y) && Near(box.Width, shifted.Width) && Near(box.Height, shifted.Height);

            // An Image may report its whole box or just the fitted picture inside it.
            Assert.True(
                Matches(expected) || Matches(PictureFit.Expected(control, expected)),
                $"{screen.Name}: {control.Name} is out of place. Page origin {origin}.{Environment.NewLine}{string.Join(Environment.NewLine, report)}");
        }
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
        Assert.True(build.ExitCode == 0, "The generated MAUI project did not build:\n" + output.Result + error.Result);
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
