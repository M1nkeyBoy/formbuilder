using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>RadioButton, ListBox, Slider, ProgressBar, DatePicker, PasswordBox, multi-line TextBox and GroupBox.</summary>
public class MoreControlsTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>One of each new control, the group box holding two radio buttons.</summary>
    private static (DesignEditor Editor, ControlDocument Group) Everything()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.ListBox, 10, 10);
        editor.AddControl(ControlType.Slider, 10, 120);
        editor.AddControl(ControlType.ProgressBar, 10, 160);
        editor.AddControl(ControlType.DatePicker, 10, 190);
        editor.AddControl(ControlType.PasswordBox, 10, 230);
        var notes = editor.AddControl(ControlType.TextBox, 10, 270);
        editor.SetIsMultiline(notes.Id, true);
        var group = editor.AddControl(ControlType.GroupBox, 300, 10);
        editor.AddControlTo(ControlType.RadioButton, group.Id, 320, 40);
        editor.AddControlTo(ControlType.RadioButton, group.Id, 320, 100);
        return (editor, editor.FindControl(group.Id)!);
    }

    [Theory]
    [InlineData(ControlType.RadioButton, 100, 20)]
    [InlineData(ControlType.ListBox, 120, 100)]
    [InlineData(ControlType.Slider, 150, 30)]
    [InlineData(ControlType.ProgressBar, 150, 20)]
    [InlineData(ControlType.DatePicker, 140, 30)]
    [InlineData(ControlType.PasswordBox, 120, 30)]
    [InlineData(ControlType.GroupBox, 220, 160)]
    public void NewControlsArePlacedWithTheirDefaults(ControlType type, int width, int height)
    {
        var editor = new DesignEditor();

        var control = editor.AddControl(type, 10, 10);

        Assert.Equal((width, height), (control.Width, control.Height));
        Assert.Equal($"{type}1", control.Name);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void DefaultPropertiesSuitEachType()
    {
        var editor = new DesignEditor();
        var radio = editor.AddControl(ControlType.RadioButton, 10, 10).Properties;
        var list = editor.AddControl(ControlType.ListBox, 10, 40).Properties;
        var slider = editor.AddControl(ControlType.Slider, 10, 150).Properties;
        var group = editor.AddControl(ControlType.GroupBox, 200, 10).Properties;
        var password = editor.AddControl(ControlType.PasswordBox, 10, 190).Properties;

        Assert.Equal(("RadioButton1", false), (radio.Text, radio.IsChecked));
        Assert.Equal(["Item 1", "Item 2", "Item 3"], list.Items!);
        Assert.Equal((0, 100, 50), (slider.Minimum, slider.Maximum, slider.Value));
        Assert.Equal(("GroupBox1", StackOrientation.Vertical, 6), (group.Text, group.Orientation, group.Spacing));
        Assert.Equal(new ControlProperties(), password);
    }

    [Fact]
    public void ChoosingARadioButtonClearsTheOthersBesideItOnly()
    {
        var editor = new DesignEditor();
        var a = editor.AddControl(ControlType.RadioButton, 10, 10);
        var b = editor.AddControl(ControlType.RadioButton, 10, 40);
        var group = editor.AddControl(ControlType.GroupBox, 300, 10);
        var inside = editor.AddControlTo(ControlType.RadioButton, group.Id, 320, 40)!;
        editor.SetIsChecked(inside.Id, true);

        editor.SetIsChecked(a.Id, true);
        editor.SetIsChecked(b.Id, true);

        Assert.False(editor.FindControl(a.Id)!.Properties.IsChecked);
        Assert.True(editor.FindControl(b.Id)!.Properties.IsChecked);
        Assert.True(editor.FindControl(inside.Id)!.Properties.IsChecked);

        // One step: undo brings back a as the chosen one.
        editor.Undo();
        Assert.True(editor.FindControl(a.Id)!.Properties.IsChecked);
        Assert.False(editor.FindControl(b.Id)!.Properties.IsChecked);
    }

    [Theory]
    [InlineData(0, 0, 0, "greater than minimum")]
    [InlineData(10, 5, 7, "greater than minimum")]
    [InlineData(0, 10, 11, "between 0 and 10")]
    [InlineData(-2_000_000, 10, 0, "between -1000000 and 1000000")]
    public void RangesMustHoldTheirValue(int minimum, int maximum, int value, string expected)
    {
        var editor = new DesignEditor();
        var slider = editor.AddControl(ControlType.Slider, 10, 10);

        Assert.Contains(expected, editor.SetRange(slider.Id, minimum, maximum, value));
        Assert.Null(editor.SetRange(slider.Id, -5, 5, -5));
        Assert.Equal((-5, 5, -5), (editor.FindControl(slider.Id)!.Properties.Minimum, editor.FindControl(slider.Id)!.Properties.Maximum, editor.FindControl(slider.Id)!.Properties.Value));
    }

    [Fact]
    public void RangeAndMultilineOnlyApplyToTheirTypes()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);

        Assert.Equal("A Button does not have a range.", editor.SetRange(button.Id, 0, 10, 5));
        Assert.Equal("A Button does not have a multi-line setting.", editor.SetIsMultiline(button.Id, true));
    }

    [Fact]
    public void ASingleLineTextBoxStoresNoMultilineValue()
    {
        var editor = new DesignEditor();
        var box = editor.AddControl(ControlType.TextBox, 10, 10);
        editor.SetIsMultiline(box.Id, true);
        editor.SetIsMultiline(box.Id, false);

        Assert.Null(editor.FindControl(box.Id)!.Properties.IsMultiline);
        Assert.DoesNotContain("isMultiline", ProjectFile.Serialize(editor.Document), StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupBoxLinesUpItsChildrenInsideItsFrame()
    {
        var (editor, group) = Everything();

        var placed = ContainerLayout.Flatten(editor.Screen).Where(p => p.ParentId == group.Id).Select(p => p.Bounds).ToList();

        // (300, 10) plus the inset (8, 20); 220 wide less 16; the second after a 6 DIP gap.
        Assert.Equal([new ControlBounds(308, 30, 204, 20), new ControlBounds(308, 56, 204, 20)], placed);
    }

    [Fact]
    public void DroppingIntoAGroupBoxUsesItsStackOrder()
    {
        var (editor, group) = Everything();
        var first = group.Children![0];

        var third = editor.AddControlTo(ControlType.CheckBox, group.Id, 320, 32)!;

        Assert.Equal([third.Id, first.Id], editor.FindControl(group.Id)!.Children!.Take(2).Select(c => c.Id));
    }

    [Fact]
    public void EveryNewControlSurvivesSaveAndOpen()
    {
        var (editor, _) = Everything();

        var reloaded = ProjectFile.Deserialize(ProjectFile.Serialize(editor.Document));

        Assert.Equal(
            ControlTree.All(editor.Screen.Controls).Select(c => (c.Type, c.Name, c.Properties.Text, c.Properties.IsMultiline, c.Properties.Value)),
            ControlTree.All(reloaded.MainScreen.Controls).Select(c => (c.Type, c.Name, c.Properties.Text, c.Properties.IsMultiline, c.Properties.Value)));
    }

    [Fact]
    public void ARangeOutsideItsLimitsIsRejectedOnLoad()
    {
        var (editor, _) = Everything();
        var json = ProjectFile.Serialize(editor.Document).Replace("\"value\": 50", "\"value\": 500", StringComparison.Ordinal);

        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains("Control \"Slider1\": Value must be between 0 and 100.", error.Message);
    }

    [Fact]
    public void WpfWritesEachNewControl()
    {
        var (editor, _) = Everything();
        var root = XDocument.Parse(WpfGenerator.WindowXaml(editor.Document, "Demo")).Root!;
        XElement Named(string name) => root.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

        Assert.Equal(["Item 1", "Item 2", "Item 3"], Named("ListBox1").Elements().Select(e => (string?)e.Attribute("Content")));
        Assert.Equal("ListBoxItem", Named("ListBox1").Elements().First().Name.LocalName);
        Assert.Equal(("0", "100", "50"), ((string?)Named("Slider1").Attribute("Minimum"), (string?)Named("Slider1").Attribute("Maximum"), (string?)Named("Slider1").Attribute("Value")));
        Assert.Equal("Slider1_ValueChanged", (string?)Named("Slider1").Attribute("ValueChanged"));
        Assert.Equal("ProgressBar", Named("ProgressBar1").Name.LocalName);
        Assert.Equal("DatePicker1_SelectedDateChanged", (string?)Named("DatePicker1").Attribute("SelectedDateChanged"));
        Assert.Equal("PasswordBox1_PasswordChanged", (string?)Named("PasswordBox1").Attribute("PasswordChanged"));
        Assert.Equal(("True", "Wrap"), ((string?)Named("TextBox1").Attribute("AcceptsReturn"), (string?)Named("TextBox1").Attribute("TextWrapping")));

        // The GroupBox draws the frame; a StackPanel inset inside the same Grid holds the children.
        var group = Named("GroupBox1");
        Assert.Equal("GroupBox1", (string?)group.Attribute("Header"));
        var holder = group.Parent!;
        Assert.Equal("Grid", holder.Name.LocalName);
        var stack = holder.Elements().Single(e => e.Name.LocalName == "StackPanel");
        Assert.Equal("8,20,8,8", (string?)stack.Attribute("Margin"));
        Assert.Equal(["RadioButton1", "RadioButton2"], stack.Elements().Select(e => (string?)e.Attribute(X + "Name")));
        Assert.Equal("RadioButton1_Click", (string?)Named("RadioButton1").Attribute("Click"));
    }

    [Fact]
    public void WpfHooksUseEachControlsEventArguments()
    {
        var (editor, _) = Everything();

        var code = WpfGenerator.EventsCode(editor.Document, "Demo");

        Assert.Contains("partial void OnSlider1ValueChanged(RoutedPropertyChangedEventArgs<double> e);", code);
        Assert.Contains("partial void OnListBox1SelectionChanged(SelectionChangedEventArgs e);", code);
        Assert.Contains("partial void OnPasswordBox1PasswordChanged(RoutedEventArgs e);", code);
        Assert.DoesNotContain("ProgressBar1", code);
        Assert.DoesNotContain("GroupBox1", code);
    }

    [Fact]
    public void WinFormsWritesEachNewControl()
    {
        var (editor, _) = Everything();

        var code = WinFormsGenerator.DesignerCode(editor.Document, "Demo");

        Assert.Contains("this.Slider1 = new System.Windows.Forms.TrackBar();", code);
        Assert.Contains("this.Slider1.AutoSize = false;", code);
        Assert.Contains("this.Slider1.Value = 50;", code);
        Assert.Contains("this.DatePicker1 = new System.Windows.Forms.DateTimePicker();", code);
        Assert.Contains("this.DatePicker1.Checked = false;", code);
        Assert.Contains("this.PasswordBox1 = new System.Windows.Forms.TextBox();", code);
        Assert.Contains("this.PasswordBox1.UseSystemPasswordChar = true;", code);
        Assert.Contains("this.TextBox1.Multiline = true;", code);
        Assert.Contains("this.ListBox1.IntegralHeight = false;", code);
        Assert.Contains("this.ListBox1.Items.AddRange(new object[] { \"Item 1\", \"Item 2\", \"Item 3\" });", code);
        Assert.Contains("this.RadioButton1.Checked = false;", code);

        // The GroupBox holds a layout panel at the fixed inset, which holds the radio buttons.
        Assert.Contains("private System.Windows.Forms.TableLayoutPanel GroupBox1Layout;", code);
        Assert.Contains("this.GroupBox1.Controls.Add(this.GroupBox1Layout);", code);
        Assert.Contains("this.GroupBox1Layout.Location = new System.Drawing.Point(8, 20);", code);
        Assert.Contains("this.GroupBox1Layout.Size = new System.Drawing.Size(204, 132);", code);
        Assert.Contains("this.GroupBox1Layout.Controls.Add(this.RadioButton1, 0, 0);", code);
        Assert.True(code.IndexOf("this.GroupBox1.Size", StringComparison.Ordinal) < code.IndexOf("this.GroupBox1.Controls.Add", StringComparison.Ordinal),
            "The GroupBox must have its size before its layout panel is added.");
    }

    [Fact]
    public void WinFormsOutputIsValidCSharp()
    {
        var (editor, _) = Everything();

        foreach (var file in WinFormsGenerator.Generate(editor.Document, "Demo").Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var errors = CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, file.RelativePath + ": " + string.Join(Environment.NewLine, errors));
        }
    }

    [Fact]
    public void AControlCannotTakeAGroupBoxLayoutPanelsName()
    {
        var (editor, _) = Everything();
        var button = editor.AddControl(ControlType.Button, 10, 500);
        editor.Rename(button.Id, "GroupBox1Layout");

        Assert.Contains(WinFormsGenerator.Check(editor.Document), p => p.Contains("layout panel generated for GroupBox \"GroupBox1\"", StringComparison.Ordinal));
        Assert.Empty(WpfGenerator.Check(editor.Document));
    }
}
