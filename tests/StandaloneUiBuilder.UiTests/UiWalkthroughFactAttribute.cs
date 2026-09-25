namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// A test that takes over the mouse and keyboard to drive the real editor. It runs only on
/// Windows and only when UIB_RUN_UI_TESTS=1, so it never hijacks someone's desktop during a
/// normal test run.
/// </summary>
public sealed class UiWalkthroughFactAttribute : FactAttribute
{
    public UiWalkthroughFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "UI walkthrough runs only on Windows.";
        }
        else if (Environment.GetEnvironmentVariable("UIB_RUN_UI_TESTS") != "1")
        {
            Skip = "Set UIB_RUN_UI_TESTS=1 to run the UI walkthrough. It moves the mouse and types.";
        }
    }
}
