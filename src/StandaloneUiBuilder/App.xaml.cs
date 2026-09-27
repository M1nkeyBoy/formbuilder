using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace StandaloneUiBuilder;

public partial class App : Application
{
    /// <summary>
    /// Where the editor notes how far startup got, and any error it could not handle, so a
    /// start that shows nothing can be diagnosed: %LOCALAPPDATA%\StandaloneUiBuilder\startup.log.
    /// </summary>
    public static string StartupLogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StandaloneUiBuilder", "startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The same program draws library controls for the editor, in a process of its own, so
        // a library's code never runs in the editor (see Preview/PreviewHost).
        if (e.Args is [Preview.PreviewHost.Argument, ..])
        {
            Preview.PreviewHost.Run(this);
            return;
        }

        StartLog();
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Report("An error the editor could not handle", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log($"Unhandled error: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"Unobserved background error: {args.Exception}");
            args.SetObserved();
        };

        try
        {
            EditorTheme.Start(this);
            Log("Theme loaded");
            var window = new MainWindow();
            Log("Main window created");
            window.Show();
            window.Activate();
            Log($"Main window shown at {window.Left:0},{window.Top:0}, {window.ActualWidth:0} × {window.ActualHeight:0}, state {window.WindowState}");
        }
        catch (Exception ex)
        {
            Report("The editor could not start", ex);
            Shutdown(1);
        }
    }

    /// <summary>Notes a startup step in the log; never fails.</summary>
    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(StartupLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void StartLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
            File.WriteAllText(StartupLogPath,
                $"Standalone UI Builder starting, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, .NET {Environment.Version}, {Environment.OSVersion}, process {Environment.ProcessPath}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Shows an error, with where the details are kept, and logs it.</summary>
    private static void Report(string summary, Exception exception)
    {
        Log($"{summary}: {exception}");
        MessageBox.Show(
            $"{summary}:{Environment.NewLine}{Environment.NewLine}{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{Environment.NewLine}"
                + $"The details are in {StartupLogPath}.",
            "Standalone UI Builder", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
