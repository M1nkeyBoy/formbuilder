using System.Diagnostics;
using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Conditions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace StandaloneUiBuilder.UiTests;

/// <summary>One running copy of the editor, with helpers to drive it like a user.</summary>
internal sealed class EditorSession : IDisposable
{
    public const string AppTitle = "Standalone UI Builder";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(45);

    private readonly UIA3Automation automation = new();
    private Window? window;

    private EditorSession(Application app) => App = app;

    public Application App { get; }

    public Window Window => window ??= FindMainWindow();

    public static string RepositoryRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "StandaloneUiBuilder.sln")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }
    }

    public static string ArtifactsDirectory
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("UIB_UI_TEST_ARTIFACTS")
                ?? Path.Combine(RepositoryRoot, "artifacts", "ui-walkthrough");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Starts the editor. Recovery drafts go to <paramref name="recoveryDirectory"/>, so a test
    /// that ends the editor abruptly cannot affect another test or the user's own drafts.
    /// </summary>
    public static EditorSession Launch(string recoveryDirectory, string? projectPath = null)
    {
#if RELEASE
        const string configuration = "Release";
#else
        const string configuration = "Debug";
#endif
        var exe = Path.Combine(RepositoryRoot, "src", "StandaloneUiBuilder", "bin", configuration, "net10.0-windows", "StandaloneUiBuilder.exe");
        var startInfo = new ProcessStartInfo(exe);
        startInfo.Environment["UIB_RECOVERY_DIR"] = recoveryDirectory;
        if (projectPath is not null)
        {
            startInfo.ArgumentList.Add(projectPath);
        }

        return new EditorSession(Application.Launch(startInfo));
    }

    public ConditionFactory Find => automation.ConditionFactory;

    public AutomationElement ById(string automationId) =>
        WaitFor(() => Window.FindFirstDescendant(Find.ByAutomationId(automationId)), $"element {automationId}");

    /// <summary>Waits for a descendant of an element that matches a condition.</summary>
    public static AutomationElement Within(AutomationElement parent, ConditionBase condition, string what) =>
        WaitFor(() => parent.FindFirstDescendant(condition), what);

    public TextBox Field(string automationId) => ById(automationId).AsTextBox();

    public string Status => ById("StatusText").Name;

    /// <summary>The automation ID of the element with keyboard focus, or its class name if it has none.</summary>
    public string FocusedAutomationId()
    {
        var focused = automation.FocusedElement();
        var id = focused.Properties.AutomationId.ValueOrDefault;
        return string.IsNullOrEmpty(id) ? focused.Properties.ClassName.ValueOrDefault ?? "" : id;
    }

    /// <summary>Screen point of a design coordinate, assuming 100% display scaling.</summary>
    public Point Canvas(int x, int y)
    {
        // The surface sits inside a 24 DIP margin and a 1 DIP border in its scroll viewer.
        var scroller = ById("SurfaceScroller").BoundingRectangle;
        return new Point(scroller.Left + 25 + x, scroller.Top + 25 + y);
    }

    public void ClickCanvas(int x, int y)
    {
        Mouse.Click(Canvas(x, y));
        Wait.UntilInputIsProcessed();
    }

    public void DragOnCanvas(int fromX, int fromY, int toX, int toY) => Drag(Canvas(fromX, fromY), Canvas(toX, toY));

    public static void Drag(Point from, Point to)
    {
        Mouse.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            Mouse.MoveTo(new Point(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps));
            Thread.Sleep(40);
        }

        Thread.Sleep(150);
        Mouse.Up(MouseButton.Left);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
    }

    /// <summary>Replaces a field's text by typing, then presses Enter to apply it.</summary>
    public void TypeInto(string automationId, string text)
    {
        Field(automationId).Click();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }

    public static void Press(params VirtualKeyShort[] keys)
    {
        Keyboard.TypeSimultaneously(keys);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
    }

    /// <summary>
    /// Closes the main window with Alt+F4. (The automation Close call can wait on the editor's
    /// modal unsaved-changes prompt.)
    /// </summary>
    public void CloseWindow()
    {
        Window.Focus();
        Press(VirtualKeyShort.ALT, VirtualKeyShort.F4);
    }

    /// <summary>Finds a button in any dialog the editor is showing.</summary>
    public Button DialogButton(string name) => WaitFor(() =>
    {
        foreach (var topLevel in automation.GetDesktop().FindAllChildren(Find.ByProcessId(App.ProcessId)))
        {
            if (topLevel.FindFirstDescendant(Find.ByName(name).And(Find.ByControlType(FlaUI.Core.Definitions.ControlType.Button))) is { } button
                && topLevel.AsWindow().IsModal)
            {
                return button.AsButton();
            }

            foreach (var modal in topLevel.AsWindow().ModalWindows)
            {
                if (modal.FindFirstDescendant(Find.ByName(name).And(Find.ByControlType(FlaUI.Core.Definitions.ControlType.Button))) is { } inner)
                {
                    return inner.AsButton();
                }
            }
        }

        return null;
    }, $"dialog button \"{name}\"");

    /// <summary>
    /// The editor's open dialog. The first Windows file or folder dialog of a run loads shell
    /// components and can take well over the usual wait on a busy machine, so it gets longer.
    /// </summary>
    public Window Dialog() => WaitFor(() => Window.ModalWindows.FirstOrDefault(), "a dialog", DialogTimeout);

    public void Screenshot(string name) => Capture.Element(Window).ToFile(Path.Combine(ArtifactsDirectory, name + ".png"));

    public static T WaitFor<T>(Func<T?> find, string what, TimeSpan? timeout = null)
        where T : class
    {
        var result = Retry.WhileNull(find, timeout ?? Timeout, TimeSpan.FromMilliseconds(200));
        if (result.Result is null)
        {
            CaptureFailure();
            throw new TimeoutException($"Timed out waiting for {what}.");
        }

        return result.Result;
    }

    public static void WaitUntil(Func<bool> condition, Func<string> describe)
    {
        var result = Retry.WhileFalse(condition, Timeout, TimeSpan.FromMilliseconds(200));
        if (!result.Result)
        {
            CaptureFailure();
        }

        Assert.True(result.Result, describe());
    }

    public void WaitForExit() => WaitUntil(() => App.HasExited, () => "The editor did not exit.");

    /// <summary>Saves what is on screen when a step fails, while the editor is still open.</summary>
    private static void CaptureFailure() =>
        Capture.Screen().ToFile(Path.Combine(ArtifactsDirectory, $"failure-{DateTime.Now:HHmmss}.png"));

    public void Dispose()
    {
        if (!App.HasExited)
        {
            App.Kill();
        }

        App.Dispose();
        automation.Dispose();
    }

    private Window FindMainWindow() => WaitFor(() =>
        automation.GetDesktop()
            .FindAllChildren(Find.ByProcessId(App.ProcessId))
            .Select(e => e.AsWindow())
            .FirstOrDefault(w => w.Title.EndsWith(AppTitle, StringComparison.Ordinal) && w.Title != AppTitle),
        "the main window");
}
