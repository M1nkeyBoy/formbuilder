using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>StackPanel and Grid containers in WPF and WinForms output.</summary>
public class ContainerOutputTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static ProjectDocument Demo() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    private static Dictionary<string, XElement> WpfElements(ProjectDocument document) =>
        XDocument.Parse(WpfGenerator.WindowXaml(document, "Demo")).Descendants()
            .Where(e => e.Attribute(Xaml + "Name") is not null)
            .ToDictionary(e => (string)e.Attribute(Xaml + "Name")!);

    [Fact]
    public void DemoSampleIsValidAndUsesEveryContainerFeature()
    {
        var document = Demo();
        var all = ControlTree.All(document.Screen.Controls).ToList();

        Assert.Empty(DocumentValidator.Validate(document));
        Assert.Contains(all, c => c.Type == ControlType.Grid);
        Assert.Contains(all, c => c.Properties.Orientation == StackOrientation.Horizontal);
        Assert.Contains(all, c => c.Type == ControlType.StackPanel && ControlTree.ParentOf(document.Screen.Controls, c.Id) is not null);
        Assert.Empty(WpfGenerator.Check(document));
        Assert.Empty(WinFormsGenerator.Check(document));
    }

    [Fact]
    public void WpfNestsChildrenInsideTheirContainers()
    {
        var elements = WpfElements(Demo());

        Assert.Equal("StackPanel", elements["FieldsStack"].Name.LocalName);
        Assert.Equal("Vertical", (string?)elements["FieldsStack"].Attribute("Orientation"));
        Assert.Equal("FieldsStack", (string?)elements["NameTextBox"].Parent!.Attribute(Xaml + "Name"));
        Assert.Equal("OptionsStack", (string?)elements["SecondOption"].Parent!.Attribute(Xaml + "Name"));
        Assert.Equal("ButtonGrid", (string?)elements["OptionsStack"].Parent!.Attribute(Xaml + "Name"));
    }

    [Fact]
    public void WpfStackChildrenKeepTheirSizeAlongTheStackWithSpacingAsMargin()
    {
        var elements = WpfElements(Demo());

        var first = elements["NameLabel"];
        var second = elements["NameTextBox"];
        Assert.Equal("Stretch", (string?)first.Attribute("HorizontalAlignment"));
        Assert.Equal("20", (string?)first.Attribute("Height"));
        Assert.Null(first.Attribute("Margin"));
        Assert.Null(first.Attribute("Width"));
        Assert.Equal("0,8,0,0", (string?)second.Attribute("Margin"));

        var footerSecond = elements["CancelFooterButton"];
        Assert.Equal("100", (string?)footerSecond.Attribute("Width"));
        Assert.Equal("10,0,0,0", (string?)footerSecond.Attribute("Margin"));
        Assert.Null(footerSecond.Attribute("Height"));
    }

    [Fact]
    public void WpfGridHasEqualRowsAndColumnsAndChildrenInCells()
    {
        var elements = WpfElements(Demo());
        var grid = elements["ButtonGrid"];

        Assert.Equal(3, grid.Element(Presentation + "Grid.RowDefinitions")!.Elements().Count());
        Assert.Equal(2, grid.Element(Presentation + "Grid.ColumnDefinitions")!.Elements().Count());
        Assert.All(grid.Descendants(Presentation + "RowDefinition"), r => Assert.Equal("*", (string?)r.Attribute("Height")));
        Assert.Equal(("1", "1"), ((string?)elements["FourButton"].Attribute("Grid.Row"), (string?)elements["FourButton"].Attribute("Grid.Column")));
        Assert.Null(elements["FourButton"].Attribute("Width"));
    }

    [Fact]
    public void WpfEventHooksCoverControlsInsideContainers()
    {
        var events = WpfGenerator.EventsCode(Demo(), "Demo");

        Assert.Contains("partial void OnFourButtonClick(RoutedEventArgs e);", events);
        Assert.Contains("partial void OnSecondOptionClick(RoutedEventArgs e);", events);
        Assert.DoesNotContain("ButtonGrid", events);
    }

    [Fact]
    public void EmptyGridStillHasItsRowsAndColumns()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Grid, 0, 0);

        var grid = WpfElements(editor.Document)["Grid1"];

        Assert.Equal(2, grid.Descendants(Presentation + "RowDefinition").Count());
    }

    [Fact]
    public void WinFormsContainersBecomeTableLayoutPanels()
    {
        var code = WinFormsGenerator.DesignerCode(Demo(), "Demo");

        Assert.Contains("this.FieldsStack = new System.Windows.Forms.TableLayoutPanel();", code);
        Assert.Contains("private System.Windows.Forms.TableLayoutPanel ButtonGrid;", code);

        // Vertical stack: one column, a fixed row per child (holding the gap before it), then a filler row.
        Assert.Contains("this.FieldsStack.RowCount = 6;", code);
        Assert.Contains("this.FieldsStack.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));", code);
        Assert.Contains("this.FieldsStack.Controls.Add(this.NameTextBox, 0, 1);", code);
        Assert.Contains("this.NameTextBox.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);", code);
        Assert.Contains("this.NameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;", code);
        Assert.DoesNotContain("this.NameTextBox.Location", code);

        // Horizontal stack: columns instead of rows.
        Assert.Contains("this.FooterStack.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 110F));", code);

        // Grid: equal percentage rows and columns; children added at their cells (column, row).
        Assert.Contains("this.ButtonGrid.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.3333F));", code);
        Assert.Contains("this.ButtonGrid.Controls.Add(this.FourButton, 1, 1);", code);
        Assert.Contains("this.ButtonGrid.Controls.Add(this.OptionsStack, 1, 2);", code);
        Assert.Contains("this.ButtonGrid.SuspendLayout();", code);
        Assert.Contains("this.ButtonGrid.ResumeLayout(false);", code);

        // Only controls directly on the form are added to it.
        Assert.Contains("this.Controls.Add(this.ButtonGrid);", code);
        Assert.DoesNotContain("this.Controls.Add(this.FourButton);", code);
    }

    [Fact]
    public void WinFormsOutputWithContainersIsValidCSharp()
    {
        foreach (var file in WinFormsGenerator.Generate(Demo(), "Demo").Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var errors = CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, file.RelativePath + ": " + string.Join(Environment.NewLine, errors));
        }
    }

    [Fact]
    public void WinFormsEventHooksCoverControlsInsideContainers()
    {
        var designer = WinFormsGenerator.DesignerCode(Demo(), "Demo");
        var events = WinFormsGenerator.EventsCode(Demo(), "Demo");

        Assert.Contains("this.ThemeComboBox.SelectedIndexChanged += this.ThemeComboBox_SelectedIndexChanged;", designer);
        Assert.Contains("partial void OnFirstOptionClick(System.EventArgs e);", events);
    }

    [Fact]
    public void NameClashesInsideContainersAreReported()
    {
        var editor = new DesignEditor();
        var stack = editor.AddControl(ControlType.StackPanel, 0, 0);
        var inside = editor.AddControlTo(ControlType.Button, stack.Id, 10, 10)!;
        editor.Rename(inside.Id, "Content");

        Assert.NotEmpty(WpfGenerator.Check(editor.Document));
    }

    [Fact]
    public void SpansAreWrittenForBothTargets()
    {
        var xaml = WpfElements(Demo());
        var winforms = WinFormsGenerator.DesignerCode(Demo(), "Demo");

        Assert.Equal("2", (string?)xaml["OneButton"].Attribute("Grid.ColumnSpan"));
        Assert.Null(xaml["OneButton"].Attribute("Grid.RowSpan"));
        Assert.Null(xaml["FourButton"].Attribute("Grid.ColumnSpan"));
        Assert.Contains("this.ButtonGrid.SetColumnSpan(this.OneButton, 2);", winforms);
        Assert.DoesNotContain("SetRowSpan", winforms);
    }
}
