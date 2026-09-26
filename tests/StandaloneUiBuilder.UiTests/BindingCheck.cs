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
            if (progress.Patterns.RangeValue.TryGetPattern(out var range))
            {
                return (range.Value.Value - range.Minimum.Value) / (range.Maximum.Value - range.Minimum.Value);
            }

            // Windows Forms' progress bar reports only its accessible value, a percentage ("30%").
            var text = progress.Patterns.Value.TryGetPattern(out var value) ? value.Value.Value
                : progress.Patterns.LegacyIAccessible.Pattern.Value.Value;
            return double.Parse(text.Trim().TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100;
        }

        EditorSession.WaitUntil(() => Math.Abs(Share() - 0.3) < 0.01, () => $"The progress bar starts at {Share():P0}, not 30%.");
        if (slider.Patterns.RangeValue.TryGetPattern(out var sliderRange))
        {
            sliderRange.SetValue(7);
        }
        else
        {
            // Windows Forms' TrackBar offers no RangeValue either; move it with the keyboard, a step a press.
            slider.Focus();
            for (var i = 0; i < 4; i++)
            {
                FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.RIGHT);
                FlaUI.Core.Input.Wait.UntilInputIsProcessed();
            }
        }
        EditorSession.WaitUntil(() => Math.Abs(Share() - 0.7) < 0.01, () => $"The progress bar shows {Share():P0} after the slider moved to 7 of 10.");
    }
}
