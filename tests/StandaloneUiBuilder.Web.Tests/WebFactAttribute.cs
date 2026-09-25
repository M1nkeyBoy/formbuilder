namespace StandaloneUiBuilder.Web.Tests;

/// <summary>
/// A test that builds and runs a generated web app and drives a browser. It runs only when
/// UIB_RUN_WEB_TESTS=1, and needs Edge (Windows) or a Chromium given by UIB_CHROMIUM.
/// </summary>
public sealed class WebFactAttribute : FactAttribute
{
    public WebFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UIB_RUN_WEB_TESTS") != "1")
        {
            Skip = "Set UIB_RUN_WEB_TESTS=1 to build, run and check generated web apps in a browser.";
        }
    }
}
