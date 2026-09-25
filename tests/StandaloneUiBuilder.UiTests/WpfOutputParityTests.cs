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
    // XamlReader cannot load x:Class or event handler attributes; those need compiled XAML.
    [GeneratedRegex(@"\s+(x:Class|Click|TextChanged|SelectionChanged)=""[^""]*""")]
    private static partial Regex CompiledOnlyAttributes();

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

        var result = WpfExporter.Export(Sample(), Path.Combine(parent, "wpf"));

        // Implement a hook the way a developer would, so the CI build proves the wiring compiles.
        var codeBehind = Path.Combine(result.ProjectFolder, "MainWindow.xaml.cs");
        var code = File.ReadAllText(codeBehind).TrimEnd();
        if (!code.Contains("partial void OnSubmitButtonClick(RoutedEventArgs e) =>", StringComparison.Ordinal))
        {
            code = code[..^1] + "    partial void OnSubmitButtonClick(RoutedEventArgs e) => Title = \"Submitted \" + NameTextBox.Text;\n}\n";
            File.WriteAllText(codeBehind, code);
        }

        Assert.True(File.Exists(Path.Combine(result.ProjectFolder, "CustomerForm.csproj")));
    }

    [WindowsFact]
    public void AnchoredDesignLaysOutTheSameEverywhere() => RunOnStaThread(() => AssertParity(AnchorMix()));

    /// <summary>One control per interesting anchor combination.</summary>
    private static ProjectDocument AnchorMix()
    {
        var editor = new DesignEditor();
        var anchors = new[]
        {
            AnchorEdges.Default,
            AnchorEdges.Right | AnchorEdges.Top,
            AnchorEdges.Left | AnchorEdges.Bottom,
            AnchorEdges.Right | AnchorEdges.Bottom,
            AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top,
            AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Bottom,
            AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top | AnchorEdges.Bottom,
        };
        var types = ControlCatalog.All.Select(d => d.Type).ToArray();
        for (var i = 0; i < anchors.Length; i++)
        {
            var control = editor.AddControl(types[i % types.Length], 40 + i * 100, 40 + i * 70);
            editor.SetAnchor(control.Id, anchors[i]);
        }

        return editor.Document;
    }

    /// <summary>
    /// Compares, control by control: the generated XAML as WPF lays it out; the designer's
    /// Preview as WPF lays it out; and the Core anchor rules. At the design size and, for a
    /// resizable screen, at a larger size.
    /// </summary>
    private static void AssertParity(ProjectDocument document)
    {
        var screen = document.Screen;
        var xaml = CompiledOnlyAttributes().Replace(WpfGenerator.WindowXaml(document, "Parity"), "");
        var window = (Window)XamlReader.Parse(xaml);
        var grid = (Grid)window.Content;
        Assert.Equal(screen.Controls.Count, grid.Children.Count);

        var preview = new DesignSurface();
        preview.Render(screen, selectedId: null, isPreview: true);
        _ = new Window { Content = preview };

        var sizes = new List<(int Width, int Height)> { (screen.Width, screen.Height) };
        if (AnchorLayout.IsResizable(screen))
        {
            sizes.Add((screen.Width + 230, screen.Height + 140));
        }

        foreach (var (width, height) in sizes)
        {
            Arrange(grid, width, height);
            preview.Width = width;
            preview.Height = height;
            Arrange(preview, width, height);
            var previewHosts = preview.Children.OfType<Grid>().Single(g => g.Children.Count == screen.Controls.Count && g.Children.OfType<Border>().Any()).Children;

            for (var i = 0; i < screen.Controls.Count; i++)
            {
                var control = screen.Controls[i];
                var expected = AnchorLayout.Resolve(screen, control, width, height);
                var what = $"{control.Name} ({control.Anchor}) at {width} × {height}";
                Assert.True(expected == BoundsOf((FrameworkElement)grid.Children[i]), $"{what}: generated window has {BoundsOf((FrameworkElement)grid.Children[i])}, expected {expected}");
                Assert.True(expected == BoundsOf((FrameworkElement)previewHosts[i]), $"{what}: preview has {BoundsOf((FrameworkElement)previewHosts[i])}, expected {expected}");
            }
        }

        // Control-level appearance: the same padding, alignment, text and values as the designer.
        var designedControls = screen.Controls.Select(c => ControlFactory.Create(c, buttonClicked: null)).ToList();
        var designCanvas = new Canvas();
        designedControls.ForEach(c => designCanvas.Children.Add(c));
        _ = new Window { Content = designCanvas };

        for (var i = 0; i < screen.Controls.Count; i++)
        {
            var control = screen.Controls[i];
            var generated = (FrameworkElement)grid.Children[i];
            var designed = designedControls[i];
            var what = $"{control.Name} ({control.Type})";

            Assert.Equal(designed.GetType(), generated.GetType());
            Assert.Equal(control.Name, generated.Name);

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

    private static void Arrange(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static ControlBounds BoundsOf(FrameworkElement element)
    {
        var offset = System.Windows.Media.VisualTreeHelper.GetOffset(element);
        return new ControlBounds(
            (int)Math.Round(offset.X),
            (int)Math.Round(offset.Y),
            (int)Math.Round(element.RenderSize.Width),
            (int)Math.Round(element.RenderSize.Height));
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
