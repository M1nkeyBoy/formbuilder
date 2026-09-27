using System.Windows.Controls;
using System.Windows.Data;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder;

/// <summary>The Screen section's Device list: popular phone, tablet and desktop sizes.</summary>
public partial class MainWindow
{
    /// <summary>An entry of the Device list; Custom has no preset.</summary>
    private sealed record ScreenSizeChoice(string Label, string Category, ScreenSizePreset? Preset)
    {
        // The closed list shows the chosen entry as text.
        public override string ToString() => Label;
    }

    private static readonly ScreenSizeChoice CustomSize = new("Custom", "", null);

    private string? screenSizeError;

    // The preset last chosen, so a device sharing its size with another keeps its own name.
    private ScreenSizePreset? chosenPreset;

    /// <summary>Fills the Device list once: Custom, then the presets grouped as phones, tablets and desktops.</summary>
    private void SetUpScreenSizes()
    {
        var choices = new List<ScreenSizeChoice> { CustomSize };
        choices.AddRange(ScreenSizePresets.All.Select(p => new ScreenSizeChoice(p.Label, p.Category.ToUpperInvariant(), p)));
        var view = new CollectionViewSource { Source = choices };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScreenSizeChoice.Category)));
        ScreenSizeBox.ItemsSource = view.View;
    }

    /// <summary>Shows the preset the screen's size matches, or Custom.</summary>
    private void ShowScreenSize()
    {
        if (ScreenSizeBox.ItemsSource is null)
        {
            SetUpScreenSizes();
        }

        var (width, height) = (editor.Screen.Width, editor.Screen.Height);
        var preset = chosenPreset is { } chosen && chosen.Width == width && chosen.Height == height
            ? chosen
            : ScreenSizePresets.Find(width, height);
        refreshingInspector = true;
        ScreenSizeBox.SelectedItem = ScreenSizeBox.Items.Cast<ScreenSizeChoice>().First(c => c.Preset == preset);
        refreshingInspector = false;
    }

    private void ScreenSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingInspector || ScreenSizeBox.SelectedItem is not ScreenSizeChoice { Preset: { } preset })
        {
            return;
        }

        screenSizeError = editor.SetScreenSize(preset.Width, preset.Height);
        if (screenSizeError is null)
        {
            chosenPreset = preset;
            SetFieldError(ScreenWidthBox, null);
            SetFieldError(ScreenHeightBox, null);
            StatusText.Text = $"Screen size: {preset.Name}, {preset.Width} × {preset.Height}";
        }
        else
        {
            screenSizeError = $"{preset.Name}: {screenSizeError}";
            UpdateInspectorErrors();
        }

        // Back to what the screen is, if the size could not be applied.
        RefreshInspector();
    }
}
