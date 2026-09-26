using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Import.Wpf;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>The sample's Settings screen has a TabControl, DetailsTabs, with Notes and Advanced pages.</summary>
public class TabOutputTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace X2009 = "http://schemas.microsoft.com/winfx/2009/xaml";

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    private static ScreenDocument Settings(ProjectDocument document) => document.Screens[1];

    private static XElement Named(XElement root, XNamespace x, string name) =>
        root.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);

    private static void AssertParses(string code)
    {
        var errors = CSharpSyntaxTree.ParseText(code).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void WpfHasARealTabControlWithPagesAtTheInsetBesideIt()
    {
        var document = Sample();
        var root = XDocument.Parse(WpfGenerator.WindowXaml(document, Settings(document), "Demo")).Root!;

        var tabs = Named(root, X, "DetailsTabs");
        Assert.Equal("TabControl", tabs.Name.LocalName);
        Assert.Equal("0", (string?)tabs.Attribute("SelectedIndex"));
        Assert.Equal(["Notes", "Advanced"], tabs.Elements().Select(t => (string?)t.Attribute("Header")));
        var notes = Named(root, X, "NotesPage");
        var advanced = Named(root, X, "AdvancedPage");
        Assert.Same(tabs.Parent, notes.Parent);
        Assert.Equal("8,36,8,8", (string?)notes.Attribute("Margin"));
        Assert.Null(notes.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)advanced.Attribute("Visibility"));
        Assert.Equal("#F3F6FA", (string?)advanced.Attribute("Background"));

        var events = WpfGenerator.EventsCode(document, Settings(document), "Demo");
        Assert.Contains("AdvancedPage.Visibility = DetailsTabs.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;", events);
        Assert.Contains("partial void OnDetailsTabsSelectionChanged(SelectionChangedEventArgs e);", events);
        AssertParses(events);
    }

    [Fact]
    public void WinFormsPutsTheTabControlAndPagesInAHostPanel()
    {
        var document = Sample();
        var code = WinFormsGenerator.DesignerCode(document, Settings(document), "Demo");

        Assert.Contains("this.DetailsTabsHost = new System.Windows.Forms.Panel();", code);
        Assert.Contains("this.DetailsTabs = new System.Windows.Forms.TabControl();", code);
        Assert.Contains("this.AdvancedPage = new System.Windows.Forms.TableLayoutPanel();", code);
        Assert.Contains("this.DetailsTabs.TabPages.Add(\"Advanced\");", code);
        Assert.Contains("this.AdvancedPage.Visible = false;", code);
        Assert.Contains("this.AdvancedPage.Location = new System.Drawing.Point(8, 36);", code);
        Assert.Contains("this.AdvancedPage.Size = new System.Drawing.Size(404, 156);", code);
        Assert.Contains("this.Controls.Add(this.DetailsTabsHost);", code);

        // Pages go in before the TabControl, so they are on top of it.
        Assert.True(code.IndexOf("DetailsTabsHost.Controls.Add(this.AdvancedPage)", StringComparison.Ordinal)
            < code.IndexOf("DetailsTabsHost.Controls.Add(this.DetailsTabs)", StringComparison.Ordinal));
        AssertParses(code);

        var events = WinFormsGenerator.EventsCode(document, Settings(document), "Demo");
        Assert.Contains("this.NotesPage.Visible = this.DetailsTabs.SelectedIndex == 0;", events);
        AssertParses(events);
    }

    [Fact]
    public void WinFormsReportsAClashWithTheHostPanel()
    {
        var editor = new DesignEditor();
        var tabs = editor.AddControl(ControlType.TabControl, 10, 10);
        var button = editor.AddControl(ControlType.Button, 400, 400);
        editor.Rename(button.Id, tabs.Name + "Host");

        Assert.Contains(WinFormsGenerator.Check(editor.Document), p => p.Contains("panel generated for TabControl"));
    }

    [Fact]
    public void WinUIAndMauiDrawTabsAsButtonsOverAFrame()
    {
        var document = Sample();
        var winui = XDocument.Parse(WinUIGenerator.WindowXaml(document, Settings(document), "Demo")).Root!;
        var toggles = Named(winui, X, "DetailsTabs").Descendants().Where(e => e.Name.LocalName == "ToggleButton").ToList();
        Assert.Equal(["True", "False"], toggles.Select(t => (string?)t.Attribute("IsChecked")));
        Assert.All(toggles, t => Assert.Equal("DetailsTabs_SelectionChanged", (string?)t.Attribute("Click")));
        Assert.Equal("Collapsed", (string?)Named(winui, X, "AdvancedPage").Attribute("Visibility"));
        AssertParses(WinUIGenerator.WindowGeneratedCode(document, Settings(document), "Demo"));

        var maui = XDocument.Parse(MauiGenerator.PageXaml(document, Settings(document), "Demo")).Root!;
        var buttons = Named(maui, X2009, "DetailsTabs").Descendants().Where(e => e.Name.LocalName == "Button").ToList();
        Assert.Equal(["Notes", "Advanced"], buttons.Select(b => (string?)b.Attribute("Text")));
        Assert.Equal("False", (string?)Named(maui, X2009, "AdvancedPage").Attribute("IsVisible"));
        Assert.Equal("8,36,8,8", (string?)Named(maui, X2009, "NotesPage").Attribute("Margin"));
        var code = MauiGenerator.PageGeneratedCode(document, Settings(document), "Demo");
        Assert.Contains("AdvancedPage.IsVisible = index == 1;", code);
        AssertParses(code);
    }

    [Fact]
    public void BlazorHidesPagesUnlessTheirTabIsChosen()
    {
        var document = Sample();
        var markup = BlazorGenerator.PageRazor(document, Settings(document));

        Assert.Contains("@onclick=\"() => DetailsTabs_SelectionChanged(1)\">Advanced</button>", markup);
        Assert.Contains("hidden=\"@(DetailsTabs != 1)\"", markup);
        var code = BlazorGenerator.EventsCode(document, Settings(document), "Demo");
        Assert.Contains("private int DetailsTabs = 0;", code);
        AssertParses(code);
    }

    [Fact]
    public void ImportingAPlainWpfTabControlMakesAPagePerTab()
    {
        const string Xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Demo.MainWindow" Width="500" Height="400">
                <Grid>
                    <TabControl x:Name="Tabs" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="10,10,0,0" Width="300" Height="200" SelectedIndex="1">
                        <TabItem Header="One">
                            <StackPanel>
                                <Button x:Name="OkButton" Content="OK" Height="30" />
                            </StackPanel>
                        </TabItem>
                        <TabItem Header="Two">
                            <CheckBox x:Name="Check" Content="Check" />
                        </TabItem>
                        <TabItem Header="Three" />
                    </TabControl>
                </Grid>
            </Window>
            """;

        var result = WpfImporter.Import([new WpfImporter.WindowSource("MainWindow.xaml", Xaml, null)], "Demo", _ => null);

        var tabs = result.Document.MainScreen.Controls.Single();
        Assert.Equal(ControlType.TabControl, tabs.Type);
        Assert.Equal(1, tabs.Properties.SelectedTab);
        Assert.Equal(["One", "Two", "Three"], tabs.Children!.Select(p => p.Properties.Text));
        Assert.Equal("OkButton", tabs.Children![0].Children!.Single().Name);
        Assert.Equal("Check", tabs.Children![1].Children!.Single().Name);
        Assert.Empty(tabs.Children![2].Children!);
        Assert.Empty(DocumentValidator.Validate(result.Document));
    }
}
