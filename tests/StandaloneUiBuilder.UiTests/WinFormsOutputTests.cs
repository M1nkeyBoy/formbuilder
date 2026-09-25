using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinForms;

namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// Builds the exported WinForms sample, runs it and checks where each control really is, at
/// the design size and after enlarging the window, against the Core anchor rules.
/// </summary>
public sealed class WinFormsOutputTests
{
    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "customer-form.uibproj"));

    /// <summary>Exports the sample, with a hook implemented, so CI can also build and run it.</summary>
    [WindowsFact]
    public void SampleExports()
    {
        var parent = Environment.GetEnvironmentVariable("UIB_EXPORT_DIR") ?? Directory.CreateTempSubdirectory("uib-export-").FullName;
        WinFormsExporter.Export(ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj")), Path.Combine(parent, "winforms"));
        var result = WinFormsExporter.Export(Sample(), Path.Combine(parent, "winforms"));
        ImplementSubmitHook(result.ProjectFolder);

        Assert.True(File.Exists(Path.Combine(result.ProjectFolder, "MainForm.Designer.cs")));
    }

    [UiWalkthroughFact]
    public void GeneratedFormLaysOutControlsByTheirAnchors() => AssertGeneratedForm(Sample(), "CustomerForm", checkSubmitHook: true);

    /// <summary>
    /// The layout demo has a second screen. Its OK button's hook is implemented to open that
    /// screen's form, whose layout is then checked too.
    /// </summary>
    [UiWalkthroughFact]
    public void GeneratedFormLaysOutContainers() => AssertGeneratedForm(
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj")), "LayoutDemo", checkSubmitHook: false);

    private static void AssertGeneratedForm(ProjectDocument document, string projectName, bool checkSubmitHook)
    {
        var folder = WinFormsExporter.Export(document, Directory.CreateTempSubdirectory("uib-winforms-").FullName).ProjectFolder;
        if (checkSubmitHook)
        {
            ImplementSubmitHook(folder);
        }

        var secondScreen = document.Screens.Skip(1).FirstOrDefault();
        if (secondScreen is not null)
        {
            ImplementHook(folder, "OnOkButtonClick", $"new {WinFormsGenerator.ClassName(document, secondScreen)}().Show(this)");
        }

        var exe = Build(folder, projectName);

        using var automation = new FlaUI.UIA3.UIA3Automation();
        using var app = FlaUI.Core.Application.Launch(exe);
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20))
                ?? throw new InvalidOperationException("The generated form did not open.");
            var screen = document.MainScreen;

            AssertLayout(window, screen, screen.Width, screen.Height);

            // Enlarge the window; controls anchored right move and those anchored both ways stretch.
            var outer = window.BoundingRectangle;
            window.Patterns.Transform.Pattern.Resize(outer.Width + 200, outer.Height + 100);
            Thread.Sleep(500);
            var client = ClientRect(window);
            Assert.True(client.Width > screen.Width + 150, $"The window did not grow: client area is {client.Width} wide.");
            AssertLayout(window, screen, client.Width, client.Height);

            // The implemented hook runs when the button is clicked.
            if (checkSubmitHook)
            {
                EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("SubmitButton")), "Submit button").AsButton().Invoke();
                EditorSession.WaitUntil(() => window.Title.StartsWith("Submitted", StringComparison.Ordinal), () => $"Title after clicking Submit: {window.Title}");
            }

            // Another screen is its own form, opened by the developer's code.
            if (secondScreen is not null)
            {
                EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("OkButton")), "OK button").AsButton().Invoke();
                // UI Automation lists a form opened with Show(this) under its owner, not at the top level.
                var second = EditorSession.WaitFor(
                    () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == secondScreen.Name)
                        ?? window.FindFirstChild(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window).And(cf.ByName(secondScreen.Name)))?.AsWindow(),
                    $"the {secondScreen.Name} form");
                AssertLayout(second, secondScreen, secondScreen.Width, secondScreen.Height);
            }
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

    private static void AssertLayout(Window window, ScreenDocument screen, int width, int height)
    {
        var client = ClientRect(window);
        foreach (var placed in ContainerLayout.Flatten(screen, width, height))
        {
            var control = placed.Control;
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(control.Name))
                ?? throw new InvalidOperationException($"{control.Name} is not in the generated form.");
            var r = element.BoundingRectangle;
            var actual = new ControlBounds(r.Left - client.Left, r.Top - client.Top, r.Width, r.Height);
            var expected = placed.Bounds;
            var what = $"{control.Name} ({control.Anchor}, depth {placed.Depth}) at {width} × {height}";

            // TableLayoutPanel rounds each percentage row and column down and gives the leftover
            // pixels to the last one (300 in three rows: 99, 99, 102), so inside a grid allow one
            // pixel per row or column; elsewhere allow one pixel.
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
            Assert.True(Near(expected.X, actual.X) && Near(expected.Y, actual.Y) && Near(expected.Width, actual.Width), $"{what}: form has {actual}, expected {expected}");

            // Single-line TextBox and ComboBox heights follow the font in WinForms.
            if (control.Type is not (Core.ControlType.TextBox or Core.ControlType.ComboBox))
            {
                Assert.True(Near(expected.Height, actual.Height), $"{what}: height {actual.Height}, expected {expected.Height}");
            }
        }
    }

    private static void ImplementSubmitHook(string folder) =>
        ImplementHook(folder, "OnSubmitButtonClick", "Text = \"Submitted \" + NameTextBox.Text");

    /// <summary>Implements one of the main form's hooks, as a developer would in MainForm.cs.</summary>
    private static void ImplementHook(string folder, string hook, string body)
    {
        var formFile = Path.Combine(folder, "MainForm.cs");
        var code = File.ReadAllText(formFile).TrimEnd();
        if (!code.Contains($"partial void {hook}(EventArgs e) =>", StringComparison.Ordinal))
        {
            File.WriteAllText(formFile, code[..^1] + $"    partial void {hook}(EventArgs e) => {body};\n}}\n");
        }
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
        Assert.True(build.ExitCode == 0, "The generated WinForms project did not build:\n" + output);
        return Path.Combine(folder, "bin", "Release", "net10.0-windows", projectName + ".exe");
    }

    private static System.Drawing.Rectangle ClientRect(Window window)
    {
        var handle = window.Properties.NativeWindowHandle.Value;
        GetClientRect(handle, out var rect);
        var origin = new NativePoint();
        ClientToScreen(handle, ref origin);
        return new System.Drawing.Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);
}
