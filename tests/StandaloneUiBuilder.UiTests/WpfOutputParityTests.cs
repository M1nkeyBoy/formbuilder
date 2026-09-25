using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
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
    // An event attribute's value is its handler, named <Control>_<Event>. A picture's Source
    // is a resource of the compiled application.
    [GeneratedRegex(@"\s+(x:Class=""[^""]*""|Source=""Assets/[^""]*""|(\w+)=""\w+_\2"")")]
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

        WpfExporter.Export(ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj")), Path.Combine(parent, "wpf"));
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
        var types = ControlCatalog.All.Where(d => !d.IsContainer).Select(d => d.Type).ToArray();
        for (var i = 0; i < anchors.Length; i++)
        {
            var control = editor.AddControl(types[i % types.Length], 40 + i * 100, 40 + i * 70);
            editor.SetAnchor(control.Id, anchors[i]);
        }

        return editor.Document;
    }

    [WindowsFact]
    public void LayoutDemoLaysOutTheSameEverywhere() => RunOnStaThread(() => AssertParity(
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"))));

    /// <summary>
    /// Compares, control by control and including controls inside containers: the generated
    /// XAML as WPF lays it out; the designer's Preview as WPF lays it out; and the Core layout
    /// rules. At the design size and, for a resizable screen, at a larger size. Every screen.
    /// </summary>
    private static void AssertParity(ProjectDocument document)
    {
        foreach (var screen in document.Screens)
        {
            AssertParity(document, screen);
        }
    }

    private static void AssertParity(ProjectDocument document, ScreenDocument screen)
    {
        var xaml = CompiledOnlyAttributes().Replace(WpfGenerator.WindowXaml(document, screen, "Parity"), "");
        var window = (Window)XamlReader.Parse(xaml);
        var grid = (Grid)window.Content;
        Assert.Equal(screen.Controls.Count, grid.Children.Count);

        var preview = new DesignSurface();
        preview.Render(screen, [], isPreview: true);
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

            foreach (var expected in ContainerLayout.Flatten(screen, width, height))
            {
                var name = expected.Control.Name;
                var what = $"{name} ({expected.Control.Anchor}, depth {expected.Depth}) at {width} × {height}";
                var generated = (FrameworkElement)window.FindName(name);
                var previewed = FindByName(preview, name) ?? throw new InvalidOperationException($"{name} is not in the preview.");
                var inWindow = BoundsWithin(generated, grid);
                var inPreview = BoundsWithin(previewed, preview);
                Assert.True(Near(expected.Bounds, inWindow), $"{what}: generated window has {inWindow}, expected {expected.Bounds}");
                Assert.True(Near(expected.Bounds, inPreview), $"{what}: preview has {inPreview}, expected {expected.Bounds}");
            }
        }

        // Control-level appearance: the same padding, alignment, text and values as the designer.
        var leaves = ControlTree.All(screen.Controls).Where(c => c.Children is null).ToList();
        var designedControls = leaves.Select(c => ControlFactory.Create(c, buttonClicked: null)).ToList();
        var designCanvas = new Canvas();
        designedControls.ForEach(c => designCanvas.Children.Add(c));
        _ = new Window { Content = designCanvas };

        for (var i = 0; i < leaves.Count; i++)
        {
            var control = leaves[i];
            var generated = (FrameworkElement)window.FindName(control.Name);
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
            if (designed is WpfControl designedStyle && generated is WpfControl generatedStyle)
            {
                Assert.True(
                    (designedStyle.FontSize, designedStyle.FontWeight, designedStyle.Foreground?.ToString(), designedStyle.Background?.ToString())
                        == (generatedStyle.FontSize, generatedStyle.FontWeight, generatedStyle.Foreground?.ToString(), generatedStyle.Background?.ToString()),
                    $"{what}: font and colours differ from the designer");
            }

            switch (designed)
            {
                case System.Windows.Controls.Primitives.ToggleButton designedToggle:
                    Assert.Equal(designedToggle.IsChecked, ((System.Windows.Controls.Primitives.ToggleButton)generated).IsChecked);
                    break;
                case ListBox designedList:
                    Assert.Equal(designedList.ItemsSource.Cast<string>(), ((ListBox)generated).Items.Cast<ListBoxItem>().Select(item => (string)item.Content));
                    break;
                case System.Windows.Controls.Primitives.RangeBase designedRange:
                    var generatedRange = (System.Windows.Controls.Primitives.RangeBase)generated;
                    Assert.Equal((designedRange.Minimum, designedRange.Maximum, designedRange.Value), (generatedRange.Minimum, generatedRange.Maximum, generatedRange.Value));
                    break;
                case TextBox designedText:
                    Assert.Equal((designedText.AcceptsReturn, designedText.TextWrapping), (((TextBox)generated).AcceptsReturn, ((TextBox)generated).TextWrapping));
                    break;
                case ComboBox designedCombo:
                    var designedItems = designedCombo.ItemsSource.Cast<string>();
                    var generatedItems = ((ComboBox)generated).Items.Cast<ComboBoxItem>().Select(item => (string)item.Content);
                    Assert.Equal(designedItems, generatedItems);
                    break;
            }
        }
    }

    // WPF rounds star-sized grid cells its own way when a size does not divide evenly, so allow
    // one pixel; everything else lands exactly.
    private static bool Near(ControlBounds a, ControlBounds b) =>
        Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1 && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1;

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var childName } element && childName == name)
            {
                return element;
            }

            if (FindByName(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static ControlBounds BoundsWithin(FrameworkElement element, Visual ancestor)
    {
        var origin = element.TransformToAncestor(ancestor).Transform(new Point(0, 0));
        return new ControlBounds(
            (int)Math.Round(origin.X),
            (int)Math.Round(origin.Y),
            (int)Math.Round(element.RenderSize.Width),
            (int)Math.Round(element.RenderSize.Height));
    }

    private static void Arrange(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
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
