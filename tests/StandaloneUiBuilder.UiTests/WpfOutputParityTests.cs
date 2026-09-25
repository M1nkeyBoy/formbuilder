using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Design;
using StandaloneUiBuilder.Output.Wpf;
using WpfControl = System.Windows.Controls.Control;

namespace StandaloneUiBuilder.UiTests;

/// <summary>
/// Loads generated XAML into real WPF and checks that every control matches what the
/// designer shows for the same design.
/// </summary>
public sealed partial class WpfOutputParityTests
{
    [GeneratedRegex(@"\s+x:Class=""[^""]*""")]
    private static partial Regex ClassAttribute();

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "customer-form.uibproj"));

    /// <summary>A design that exercises escaping, underscores and every control type.</summary>
    private static ProjectDocument Tricky()
    {
        var editor = new DesignEditor();
        var label = editor.AddControl(Core.ControlType.Label, 10, 10);
        var button = editor.AddControl(Core.ControlType.Button, 10, 50);
        var box = editor.AddControl(Core.ControlType.TextBox, 10, 90);
        var check = editor.AddControl(Core.ControlType.CheckBox, 10, 130);
        var combo = editor.AddControl(Core.ControlType.ComboBox, 10, 170);
        editor.SetText(label.Id, "Name_with_underscores & <tags>");
        editor.SetText(button.Id, "Save_As \"quoted\"");
        editor.SetText(box.Id, "{Binding Not}");
        editor.SetText(check.Id, "I agree_");
        editor.SetIsChecked(check.Id, true);
        editor.SetItems(combo.Id, ["A_1", "<b>", "{x}"]);
        return editor.Document;
    }

    [WindowsFact]
    public void SampleMatchesTheDesigner() => RunOnStaThread(() => AssertParity(Sample()));

    [WindowsFact]
    public void SpecialTextMatchesTheDesigner() => RunOnStaThread(() => AssertParity(Tricky()));

    /// <summary>Exports the sample so CI can build and run the generated application.</summary>
    [WindowsFact]
    public void SampleExports()
    {
        var parent = Environment.GetEnvironmentVariable("UIB_EXPORT_DIR") ?? Directory.CreateTempSubdirectory("uib-export-").FullName;

        var result = WpfExporter.Export(Sample(), parent);

        Assert.True(File.Exists(Path.Combine(result.ProjectFolder, "CustomerForm.csproj")));
    }

    private static void AssertParity(ProjectDocument document)
    {
        var xaml = ClassAttribute().Replace(WpfGenerator.WindowXaml(document, "Parity"), "", 1);
        var window = (Window)XamlReader.Parse(xaml);
        var canvas = (Canvas)window.Content;

        Assert.Equal(document.Screen.Width, canvas.Width);
        Assert.Equal(document.Screen.Height, canvas.Height);
        Assert.Equal(document.Screen.Controls.Count, canvas.Children.Count);

        // Host the designer's controls in a window too, as the editor does, so both sides get
        // the same theme styles before they are compared.
        var designedControls = document.Screen.Controls.Select(c => ControlFactory.Create(c, buttonClicked: null)).ToList();
        var designCanvas = new Canvas();
        designedControls.ForEach(c => designCanvas.Children.Add(c));
        _ = new Window { Content = designCanvas };

        for (var i = 0; i < document.Screen.Controls.Count; i++)
        {
            var control = document.Screen.Controls[i];
            var generated = (FrameworkElement)canvas.Children[i];
            var designed = designedControls[i];
            var what = $"{control.Name} ({control.Type})";

            Assert.Equal(designed.GetType(), generated.GetType());
            Assert.Equal(control.Name, generated.Name);
            Assert.Equal(control.X, Canvas.GetLeft(generated));
            Assert.Equal(control.Y, Canvas.GetTop(generated));
            Assert.Equal(designed.Width, generated.Width);
            Assert.Equal(designed.Height, generated.Height);

            if (designed is WpfControl designedControl && generated is WpfControl generatedControl)
            {
                Assert.True(designedControl.Padding == generatedControl.Padding, $"{what}: padding {generatedControl.Padding} vs designer {designedControl.Padding}");
                Assert.True(designedControl.VerticalContentAlignment == generatedControl.VerticalContentAlignment, $"{what}: vertical content alignment");
                Assert.True(designedControl.HorizontalContentAlignment == generatedControl.HorizontalContentAlignment, $"{what}: horizontal content alignment");
            }

            Assert.Equal(DisplayedText(designed), DisplayedText(generated));

            switch (designed)
            {
                case CheckBox designedCheck:
                    Assert.Equal(designedCheck.IsChecked, ((CheckBox)generated).IsChecked);
                    break;
                case ComboBox designedCombo:
                    var designedItems = designedCombo.ItemsSource.Cast<string>();
                    var generatedItems = ((ComboBox)generated).Items.Cast<ComboBoxItem>().Select(item => (string)item.Content);
                    Assert.Equal(designedItems, generatedItems);
                    break;
            }
        }
    }

    /// <summary>The text a user sees: TextBox text, or content with access-key underscores resolved.</summary>
    private static string? DisplayedText(FrameworkElement element) => element switch
    {
        TextBox box => box.Text,
        ContentControl { Content: TextBlock block } => block.Text,
        ContentControl { Content: string text } => AccessTextDisplay(text),
        _ => null,
    };

    // WPF shows "__" as "_" and hides a single "_" (it marks the access key).
    private static string AccessTextDisplay(string text)
    {
        var shown = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '_' && i + 1 < text.Length && text[i + 1] == '_')
            {
                shown.Append('_');
                i++;
            }
            else if (text[i] != '_')
            {
                shown.Append(text[i]);
            }
        }

        return shown.ToString();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
