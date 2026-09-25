namespace StandaloneUiBuilder.UiTests;

/// <summary>A test that needs Windows (for example real WPF) but does not touch the mouse or keyboard.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Runs only on Windows.";
        }
    }
}
