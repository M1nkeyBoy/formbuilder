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

    // A cold start of the editor on a busy CI runner has taken over 15 seconds.
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(45);

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
    /// <summary>
    /// Starts the editor. Without a project it would ask which platform the new project is for;
    /// unless <paramref name="askPlatform"/> is true, the test hook answers "any platform" instead.
    /// </summary>
    public static EditorSession Launch(string recoveryDirectory, string? projectPath = null, bool askPlatform = false)
    {
#if RELEASE
        const string configuration = "Release";
#else
        const string configuration = "Debug";
#endif
        var exe = Path.Combine(RepositoryRoot, "src", "StandaloneUiBuilder", "bin", configuration, "net10.0-windows", "StandaloneUiBuilder.exe");
        var startInfo = new ProcessStartInfo(exe);
        startInfo.Environment["UIB_RECOVERY_DIR"] = recoveryDirectory;
        if (!askPlatform)
        {
            startInfo.Environment["UIB_START_PLATFORM"] = "Any";
        }
        if (projectPath is not null)
        {
            startInfo.ArgumentList.Add(projectPath);
        }

        return new EditorSession(Application.Launch(startInfo));
    }

    public ConditionFactory Find => automation.ConditionFactory;

    /// <summary>The editor's windows, including pop-ups such as the code window's suggestions.</summary>
    public Window[] TopLevelWindows => App.GetAllTopLevelWindows(automation);

    /// <summary>Saves the whole screen, which shows every window, not only the main one.</summary>
    public static void ScreenshotScreen(string name) => Capture.Screen().ToFile(Path.Combine(ArtifactsDirectory, name + ".png"));

    public AutomationElement ById(string automationId) =>
        Reveal(WaitFor(() => Window.FindFirstDescendant(Find.ByAutomationId(automationId)), $"element {automationId}"));

    /// <summary>Waits for a descendant of an element that matches a condition.</summary>
    public static AutomationElement Within(AutomationElement parent, ConditionBase condition, string what) =>
        Reveal(WaitFor(() => parent.FindFirstDescendant(condition), what));

    /// <summary>
    /// Scrolls an element into view if it is out of sight in a scrolling panel (the toolbox,
    /// the inspector, a list), so clicks land on it rather than on whatever is below the window.
    /// WPF does not report an element clipped by a scroll viewer as offscreen, so this compares
    /// the element with the nearest scrolling ancestor. A list item (or the item holding the
    /// element) scrolls itself; anything else is focused, and the editor brings what has focus
    /// into view. Elements larger than the view (the canvas) are left alone.
    /// </summary>
    private static AutomationElement Reveal(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        var item = element.Patterns.ScrollItem.IsSupported ? element : null;
        AutomationElement? viewer = null;
        var ancestor = element.Parent;
        for (var depth = 0; ancestor is not null && depth < 12; depth++, ancestor = ancestor.Parent)
        {
            if (ancestor.Patterns.Scroll.IsSupported)
            {
                viewer = ancestor;
                break;
            }

            item ??= ancestor.Patterns.ScrollItem.IsSupported ? ancestor : null;
        }

        if (viewer is null || bounds.IsEmpty)
        {
            return element;
        }

        var view = viewer.BoundingRectangle;
        var inside = bounds.Top >= view.Top && bounds.Bottom <= view.Bottom && bounds.Left >= view.Left && bounds.Right <= view.Right;
        if (inside || bounds.Height > view.Height || bounds.Width > view.Width)
        {
            return element;
        }

        if (item is not null && item.Patterns.ScrollItem.TryGetPattern(out var scrollItem))
        {
            scrollItem.ScrollIntoView();
        }
        else if (element.Properties.IsKeyboardFocusable.ValueOrDefault)
        {
            element.Focus();
        }

        Wait.UntilInputIsProcessed();
        Thread.Sleep(150);
        return element;
    }

    public TextBox Field(string automationId) => ById(automationId).AsTextBox();

    public string Status => ById("StatusText").Name;

    /// <summary>The automation ID of the element with keyboard focus, or its class name if it has none.</summary>
    public string FocusedAutomationId()
    {
        var focused = automation.FocusedElement();
        var id = focused.Properties.AutomationId.ValueOrDefault;
        return string.IsNullOrEmpty(id) ? focused.Properties.ClassName.ValueOrDefault ?? "" : id;
    }

    /// <summary>
    /// Screen point of a design coordinate, assuming 100% display scaling and zoom. The canvas
    /// is scrolled first if the point is out of sight, as on a small screen.
    /// </summary>
    public Point Canvas(int x, int y)
    {
        var surface = ShowOnCanvas(x, y, x, y);
        return new Point(surface.Left + x, surface.Top + y);
    }

    /// <summary>
    /// Scrolls the canvas, if either design point is out of sight, so the middle of the two is
    /// in the middle of the view, and returns the canvas's screen bounds.
    /// </summary>
    private Rectangle ShowOnCanvas(int x1, int y1, int x2, int y2)
    {
        var scroller = ById("SurfaceScroller");
        var view = scroller.BoundingRectangle;
        var surface = ById("Surface").BoundingRectangle;
        const int ScrollBar = 20;
        bool Visible(int x, int y) =>
            surface.Left + x >= view.Left && surface.Left + x <= view.Right - ScrollBar
            && surface.Top + y >= view.Top && surface.Top + y <= view.Bottom - ScrollBar;
        if ((!Visible(x1, y1) || !Visible(x2, y2)) && scroller.Patterns.Scroll.TryGetPattern(out var scroll))
        {
            static double Percent(double size, double viewPercent, double wanted)
            {
                var extent = size / (viewPercent / 100);
                return extent <= size ? -1 : Math.Clamp(wanted / (extent - size) * 100, 0, 100);
            }

            // The canvas sits 24 DIPs inside the scrolled area.
            var x = (x1 + x2) / 2.0 + 25;
            var y = (y1 + y2) / 2.0 + 25;
            var horizontal = scroll.HorizontallyScrollable.Value
                ? Percent(view.Width - ScrollBar, scroll.HorizontalViewSize.Value, x - (view.Width - ScrollBar) / 2.0)
                : -1;
            var vertical = scroll.VerticallyScrollable.Value
                ? Percent(view.Height - ScrollBar, scroll.VerticalViewSize.Value, y - (view.Height - ScrollBar) / 2.0)
                : -1;
            scroll.SetScrollPercent(horizontal, vertical);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(150);
            surface = ById("Surface").BoundingRectangle;
        }

        return surface;
    }

    public void ClickCanvas(int x, int y)
    {
        Mouse.Click(Canvas(x, y));
        Wait.UntilInputIsProcessed();
    }

    public void DragOnCanvas(int fromX, int fromY, int toX, int toY)
    {
        var surface = ShowOnCanvas(fromX, fromY, toX, toY);
        Drag(new Point(surface.Left + fromX, surface.Top + fromY), new Point(surface.Left + toX, surface.Top + toY));
    }

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

    /// <summary>Finds a menu item in any menu the editor has open, such as a button's drop-down menu.</summary>
    public AutomationElement PopupMenuItem(string name) => WaitFor(() =>
        automation.GetDesktop().FindAllChildren(Find.ByProcessId(App.ProcessId))
            .Select(w => w.FindFirstDescendant(Find.ByName(name).And(Find.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem))))
            .FirstOrDefault(i => i is not null), $"menu item \"{name}\"");

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

    public static void WaitUntil(Func<bool> condition, Func<string> describe, TimeSpan? timeout = null)
    {
        var result = Retry.WhileFalse(condition, timeout ?? Timeout, TimeSpan.FromMilliseconds(200));
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
        "the main window",
        LaunchTimeout);
}
