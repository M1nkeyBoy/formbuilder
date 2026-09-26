using System.Windows;

namespace StandaloneUiBuilder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        EditorTheme.Start(this);
        base.OnStartup(e);
    }
}
