using System.Windows;

namespace StandaloneUiBuilder;

public partial class App : Application
{
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

        EditorTheme.Start(this);
        new MainWindow().Show();
    }
}
