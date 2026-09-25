using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>Text size, bold text, and text and background colours.</summary>
public class StyleTests
{
    [Theory]
    [InlineData("1e6fd9", "#1E6FD9")]
    [InlineData(" #00ff7F ", "#00FF7F")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ColoursAreReadInEitherFormAndStoredCanonically(string? typed, string? stored)
    {
        Assert.True(ControlColor.TryParse(typed, out var color));
        Assert.Equal(stored, color);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    public void OtherColourTextIsRefused(string typed) => Assert.False(ControlColor.TryParse(typed, out _));

    [Fact]
    public void FontAndColoursAreSetAndUndoneAsSteps()
    {
        var editor = new DesignEditor();
        var label = editor.AddControl(ControlType.Label, 10, 10);

        Assert.Null(editor.SetFont(label.Id, 18, isBold: true));
        Assert.Null(editor.SetColors(label.Id, "c62828", "#fff8e1"));

        var properties = editor.FindControl(label.Id)!.Properties;
        Assert.Equal((18, true, "#C62828", "#FFF8E1"), (properties.FontSize, properties.IsBold, properties.Foreground, properties.Background));
        editor.Undo();
        Assert.Null(editor.FindControl(label.Id)!.Properties.Foreground);
        editor.Undo();
        Assert.Null(editor.FindControl(label.Id)!.Properties.FontSize);
    }

    [Fact]
    public void BadStylesAreRefused()
    {
        var editor = new DesignEditor();
        var label = editor.AddControl(ControlType.Label, 10, 10);
        var stack = editor.AddControl(ControlType.StackPanel, 200, 10);

        Assert.Equal("Text size must be between 6 and 72.", editor.SetFont(label.Id, 100, false));
        Assert.StartsWith("Enter colours as #RRGGBB", editor.SetColors(label.Id, "blue", null));
        Assert.Equal("A StackPanel does not have a text colour.", editor.SetColors(stack.Id, "#000000", null));
        Assert.Equal("A StackPanel does not have a font.", editor.SetFont(stack.Id, 14, false));
        Assert.Null(editor.SetColors(stack.Id, null, "#EEEEEE"));
    }

    [Fact]
    public void StylesSurviveSaveAndBadOnesAreRejectedOnLoad()
    {
        var editor = new DesignEditor();
        var label = editor.AddControl(ControlType.Label, 10, 10);
        editor.SetFont(label.Id, 18, true);
        editor.SetColors(label.Id, "#C62828", null);
        var json = ProjectFile.Serialize(editor.Document);

        Assert.Equal(18, ProjectFile.Deserialize(json).MainScreen.Controls[0].Properties.FontSize);
        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json.Replace("#C62828", "crimson", StringComparison.Ordinal)));
        Assert.Contains("\"crimson\" is not a colour", error.Message);
    }

    [Fact]
    public void EachTargetWritesTheStyle()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);
        editor.Rename(button.Id, "SaveButton");
        editor.SetFont(button.Id, 18, true);
        editor.SetColors(button.Id, "#FFFFFF", "#1E6FD9");
        var document = editor.Document;

        Assert.Contains("FontSize=\"18\" FontWeight=\"Bold\" Foreground=\"#FFFFFF\" Background=\"#1E6FD9\"", WpfGenerator.WindowXaml(document, "T"));

        var designer = WinFormsGenerator.DesignerCode(document, "T");
        Assert.Contains("this.SaveButton.Font = new System.Drawing.Font(\"Segoe UI\", 13.5F, System.Drawing.FontStyle.Bold);", designer);
        Assert.Contains("this.SaveButton.ForeColor = System.Drawing.Color.FromArgb(255, 255, 255);", designer);
        Assert.Contains("this.SaveButton.BackColor = System.Drawing.Color.FromArgb(30, 111, 217);", designer);

        Assert.Contains(";font-size:18px;font-weight:bold;color:#FFFFFF;background-color:#1E6FD9\"", BlazorGenerator.PageRazor(document));
    }

    [Fact]
    public void AGroupBoxStyleAppliesToItsTitleNotItsControls()
    {
        var editor = new DesignEditor();
        var group = editor.AddControl(ControlType.GroupBox, 10, 10);
        editor.AddControlTo(ControlType.CheckBox, group.Id, 30, 40);
        editor.SetFont(group.Id, 16, true);
        editor.SetColors(group.Id, "#C62828", "#FFF8E1");
        var document = editor.Document;

        var page = BlazorGenerator.PageRazor(document);
        Assert.Contains("<span class=\"uib-title\" style=\"font-size:16px;font-weight:bold;color:#C62828\">GroupBox1</span>", page);
        Assert.Contains("<div class=\"uib-frame\" style=\"background-color:#FFF8E1\"></div>", page);

        var designer = WinFormsGenerator.DesignerCode(document, "T");
        Assert.Contains("this.GroupBox1Layout.Font = new System.Drawing.Font(\"Segoe UI\", 9F);", designer);
        Assert.Contains("this.GroupBox1Layout.ForeColor = System.Drawing.SystemColors.ControlText;", designer);

        Assert.Contains("<GroupBox x:Name=\"GroupBox1\" Header=\"GroupBox1\" FontSize=\"16\" FontWeight=\"Bold\" Foreground=\"#C62828\" Background=\"#FFF8E1\" />", WpfGenerator.WindowXaml(document, "T"));
    }
}
