using FlaUI.Core.AutomationElements;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Checks a running layout demo's Settings screen, whose slider and progress bar are bound to the same value.</summary>
internal static class BindingCheck
{
    /// <summary>True if the screen binds the slider and progress bar together, as the layout demo does.</summary>
    public static bool Applies(ScreenDocument screen) =>
        ControlTree.All(screen.Controls).Count(c => c.Name is "LevelSlider" or "UploadProgress" && c.Properties.Binding == "Level") == 2;

    /// <summary>
    /// The progress bar starts at the slider's value and follows it when the slider moves. Both
    /// run from 0 to 10 in the design; the progress is compared as a share of its own range,
    /// since MAUI's runs from 0 to 1.
    /// </summary>
    public static void AssertSliderMovesProgress(AutomationElement window)
    {
        var slider = EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("LevelSlider")), "the slider");
        var progress = EditorSession.WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("UploadProgress")), "the progress bar");
        double Share()
        {
            var range = progress.Patterns.RangeValue.Pattern;
            return (range.Value.Value - range.Minimum.Value) / (range.Maximum.Value - range.Minimum.Value);
        }

        EditorSession.WaitUntil(() => Math.Abs(Share() - 0.3) < 0.01, () => $"The progress bar starts at {Share():P0}, not 30%.");
        slider.Patterns.RangeValue.Pattern.SetValue(7);
        EditorSession.WaitUntil(() => Math.Abs(Share() - 0.7) < 0.01, () => $"The progress bar shows {Share():P0} after the slider moved to 7 of 10.");
    }
}
