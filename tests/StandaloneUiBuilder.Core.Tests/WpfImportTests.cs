using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Import.Wpf;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

public class WpfImportTests
{
    private static ProjectDocument Sample(string name) =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj"));

    /// <summary>Everything about a control that the design determines, with screens by name.</summary>
    private static object Describe(ProjectDocument document, ScreenDocument screen, PlacedControl placed)
    {
        var c = placed.Control;
        var p = c.Properties;
        return new
        {
            c.Type, c.Name, placed.Bounds, placed.Depth,
            Anchor = placed.ParentId is null ? c.Anchor : AnchorEdges.Default,
            p.Text, p.IsChecked, Items = string.Join("|", p.Items ?? []), p.Orientation, p.Spacing, p.Rows, p.Columns,
            RowSizes = string.Join("|", p.RowSizes ?? []), ColumnSizes = string.Join("|", p.ColumnSizes ?? []),
            c.Row, c.Column, c.RowSpan, c.ColumnSpan, p.IsMultiline, p.Minimum, p.Maximum, p.Value,
            p.FontSize, p.IsBold, p.Foreground, p.Background, p.ImageData, p.Stretch, p.ClosesScreen,
            Opens = p.OpensScreen is { } id ? document.FindScreen(id)?.Name : null,
        };
    }

    private static ImportResult RoundTrip(ProjectDocument document)
    {
        var parent = Directory.CreateTempSubdirectory("uib-import-").FullName;
        try
        {
            var folder = WpfExporter.Export(document, parent).ProjectFolder;
            var windows = Directory.GetFiles(folder, "*.xaml").Where(f => !f.EndsWith("App.xaml", StringComparison.Ordinal)).Order().ToList();
            return WpfImporter.Import(windows, "Fallback");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Theory]
    [InlineData("customer-form")]
    [InlineData("layout-demo")]
    public void AnExportedDesignImportsAsItWas(string sample)
    {
        var original = Sample(sample);

        var (imported, warnings) = RoundTrip(original);

        Assert.Empty(warnings);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Screens.Select(s => (s.Name, s.Width, s.Height)), imported.Screens.Select(s => (s.Name, s.Width, s.Height)));
        for (var i = 0; i < original.Screens.Count; i++)
        {
            var expected = ContainerLayout.Flatten(original.Screens[i]).Select(p => Describe(original, original.Screens[i], p)).ToList();
            var actual = ContainerLayout.Flatten(imported.Screens[i]).Select(p => Describe(imported, imported.Screens[i], p)).ToList();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void HandWrittenXamlKeepsWhatTheBuilderUnderstandsAndReportsTheRest()
    {
        const string xaml = """
            <Window x:Class="Shop.OrderWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Title="Orders" Width="640" Height="480">
                <Canvas>
                    <Label Canvas.Left="10" Canvas.Top="10" Content="_Customer" />
                    <TextBox x:Name="CustomerBox" Canvas.Left="100" Canvas.Top="10" Width="200" Height="24" Text="Ada" />
                    <Button x:Name="PlaceButton" Canvas.Right="10" Canvas.Bottom="10" Width="90" Height="30">
                        <TextBlock Text="Place order" />
                    </Button>
                    <Expander x:Name="MoreExpander" Canvas.Left="10" Canvas.Top="60" Header="More" />
                    <CheckBox x:Name="GiftBox" Canvas.Left="10" Canvas.Top="100" Content="Gift" IsChecked="True" Foreground="Crimson" Background="#FFE0F0FF" />
                </Canvas>
            </Window>
            """;

        var (document, warnings) = WpfImporter.Import([new WpfImporter.WindowSource("Order.xaml", xaml, null)], "Fallback", _ => null);

        var screen = Assert.Single(document.Screens);
        Assert.Equal(("Order", 640, 480), (screen.Name, screen.Width, screen.Height));
        Assert.Equal("Orders", document.Name);
        Assert.Equal(["Label1", "CustomerBox", "PlaceButton", "GiftBox"], screen.Controls.Select(c => c.Name));
        Assert.Equal("Customer", screen.Controls[0].Properties.Text);
        Assert.Equal(new ControlBounds(100, 10, 200, 24), screen.Controls[1].Bounds);
        var place = screen.Controls[2];
        Assert.Equal((new ControlBounds(540, 440, 90, 30), AnchorEdges.Right | AnchorEdges.Bottom, "Place order"), (place.Bounds, place.Anchor, place.Properties.Text));
        var gift = screen.Controls[3].Properties;
        Assert.Equal((true, (string?)null, "#E0F0FF"), (gift.IsChecked, gift.Foreground, gift.Background));

        Assert.Contains("Order: Left out Expander \"MoreExpander\": the builder has no Expander.", warnings);
        Assert.Contains(warnings, w => w.Contains("\"Crimson\"", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("window's size", StringComparison.Ordinal));
    }

    [Fact]
    public void CentredControlsKeepTheirPlaceAndAutoRowsBecomeShares()
    {
        const string xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid Width="400" Height="300">
                    <Button x:Name="Middle" Width="100" Height="40" Content="Go" />
                    <Grid x:Name="Form" HorizontalAlignment="Left" VerticalAlignment="Top" Width="200" Height="100">
                        <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="2*" /></Grid.RowDefinitions>
                        <TextBox x:Name="First" Grid.Row="1" />
                    </Grid>
                </Grid>
            </Window>
            """;

        var (document, warnings) = WpfImporter.Import([new WpfImporter.WindowSource("MainWindow.xaml", xaml, null)], "Centred", _ => null);

        var screen = document.MainScreen;
        Assert.Equal(("Main", "Centred"), (screen.Name, document.Name));
        Assert.Equal(new ControlBounds(150, 130, 100, 40), screen.Controls[0].Bounds);
        Assert.Equal(["*", "2*"], screen.Controls[1].Properties.RowSizes!);
        Assert.Equal(1, screen.Controls[1].Children![0].Row);
        Assert.Contains(warnings, w => w.Contains("\"Middle\" is centred", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("sized \"Auto\"", StringComparison.Ordinal));
    }

    [Fact]
    public void FilesThatAreNotWindowsAreRefused()
    {
        var error = Assert.Throws<ImportException>(() => WpfImporter.Import(
            [new WpfImporter.WindowSource("App.xaml", "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />", null)], "X", _ => null));

        Assert.Equal("\"App.xaml\" is a Application, not a window. Choose window XAML files.", error.Message);
        Assert.Throws<ImportException>(() => WpfImporter.Import([new WpfImporter.WindowSource("Broken.xaml", "<Window", null)], "X", _ => null));
    }
}
