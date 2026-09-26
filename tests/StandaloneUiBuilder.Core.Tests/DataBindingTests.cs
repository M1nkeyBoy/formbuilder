using System.Xml.Linq;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Import.Wpf;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>The sample's Settings screen binds its values; its slider and progress bar share Level.</summary>
public class DataBindingTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    private static string File(IReadOnlyList<GeneratedFile> files, string path) =>
        files.Single(f => f.RelativePath == path).Content;

    private static string? Attribute(XElement root, string name, string attribute) =>
        (string?)root.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name).Attribute(attribute);

    [Fact]
    public void ABindingIsStoredOnlyForControlsWithAValue()
    {
        var editor = new DesignEditor();
        var box = editor.AddControl(ControlType.TextBox, 10, 10);
        var button = editor.AddControl(ControlType.Button, 10, 60);
        Assert.Null(editor.SetBinding(box.Id, "CustomerName"));
        Assert.Equal("A Button does not have a value to bind.", editor.SetBinding(button.Id, "Clicked"));

        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains("\"binding\": \"CustomerName\"", json);
        Assert.Equal("CustomerName", ProjectFile.Deserialize(json).MainScreen.Controls[0].Properties.Binding);

        // A binding on a type without a value is dropped on load, like other unused properties.
        var stray = json.Replace("\"text\": \"Button2\"", "\"text\": \"Button2\", \"binding\": \"Clicked\"", StringComparison.Ordinal);
        Assert.Null(ProjectFile.Deserialize(stray).MainScreen.Controls[1].Properties.Binding);
    }

    [Theory]
    [InlineData("customerName", "must start with a capital letter")]
    [InlineData("Customer Name", "must start with a capital letter")]
    [InlineData("PropertyChanged", "used by the generated view model")]
    [InlineData("MainViewModel", "used by the generated view model")]
    public void BindingNamesArePropertyNames(string name, string error)
    {
        var editor = new DesignEditor();
        var box = editor.AddControl(ControlType.TextBox, 10, 10);
        Assert.Contains(error, editor.SetBinding(box.Id, name));
        Assert.Null(editor.FindControl(box.Id)!.Properties.Binding);
    }

    [Fact]
    public void ControlsShareABindingOnlyForTheSameKindOfValue()
    {
        var editor = new DesignEditor();
        var box = editor.AddControl(ControlType.TextBox, 10, 10);
        var label = editor.AddControl(ControlType.Label, 10, 50);
        var check = editor.AddControl(ControlType.CheckBox, 10, 90);
        Assert.Null(editor.SetBinding(box.Id, "Name"));
        Assert.Null(editor.SetBinding(label.Id, "Name"));
        Assert.Contains("not of the same kind", editor.SetBinding(check.Id, "Name"));
        Assert.Contains("only in case", editor.SetBinding(check.Id, "NAME"));

        var property = Assert.Single(DataBindings.Properties(editor.Screen));
        Assert.Equal(("Name", BindingKind.Text), (property.Name, property.Kind));
        Assert.Equal([box.Id, label.Id], property.Controls.Select(c => c.Id));
        Assert.True(property.IsSet);
    }

    [Fact]
    public void BindingIsOneUndoStepAndBlankRemovesIt()
    {
        var editor = new DesignEditor();
        var box = editor.AddControl(ControlType.TextBox, 10, 10);
        Assert.Null(editor.SetBinding(box.Id, " Total "));
        Assert.Equal("Total", editor.FindControl(box.Id)!.Properties.Binding);
        Assert.Null(editor.SetBinding(box.Id, ""));
        Assert.Null(editor.FindControl(box.Id)!.Properties.Binding);
        editor.Undo();
        Assert.Equal("Total", editor.FindControl(box.Id)!.Properties.Binding);
    }

    [Fact]
    public void APastedBindingIsDroppedWhereTheNameHoldsAnotherKind()
    {
        var editor = new DesignEditor();
        var check = editor.AddControl(ControlType.CheckBox, 10, 10);
        var box = editor.AddControl(ControlType.TextBox, 10, 50);
        editor.SetBinding(check.Id, "Agreed");
        editor.SetBinding(box.Id, "Comment");

        var copy = editor.FindControl(box.Id)! with { Properties = editor.FindControl(box.Id)!.Properties with { Binding = "Agreed" } };
        var pasted = Assert.Single(editor.PasteControls([copy], 10));
        Assert.Null(pasted.Properties.Binding);
        Assert.Equal("Comment", Assert.Single(editor.PasteControls([editor.FindControl(box.Id)!], 10)).Properties.Binding);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void AFileWithBindingsOfDifferentKindsIsRejected()
    {
        var json = ProjectFile.Serialize(Sample()).Replace("\"binding\": \"Verbose\"", "\"binding\": \"Server\"", StringComparison.Ordinal);
        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));
        Assert.Contains("not of the same kind", error.Message);
    }

    [Fact]
    public void TheViewModelStartsFromTheDesign()
    {
        var document = Sample();
        var code = WpfGenerator.ViewModelCode(document, document.Screens[1], "Demo");
        Assert.Contains("public partial class SettingsViewModel : INotifyPropertyChanged", code);
        Assert.Contains("private string _server = \"localhost\";", code);
        Assert.Contains("private bool _useSecureConnection = true;", code);
        Assert.Contains("private double _level = 3;", code);
        Assert.Contains("private string? _logLevel;", code);
        Assert.Contains("private DateTime? _startDate;", code);
        Assert.Contains("/// <summary>Bound to LevelSlider, UploadProgress.</summary>", code);
        Assert.Contains("partial void OnLevelChanged();", code);

        // Only screens with bindings have a view model.
        Assert.DoesNotContain(WpfGenerator.Generate(document, "Demo"), f => f.RelativePath == "MainViewModel.g.cs");
        Assert.Contains(WpfGenerator.Generate(document, "Demo"), f => f.RelativePath == "SettingsViewModel.g.cs");
        Assert.Equal("\"a \\\"b\\\"\\n\"", ViewModelCode.Literal("a \"b\"\n"));
    }

    [Fact]
    public void WpfBindsThroughTheWindowsViewModel()
    {
        var document = Sample();
        var settings = document.Screens[1];
        var root = XDocument.Parse(WpfGenerator.WindowXaml(document, settings, "Demo")).Root!;
        Assert.Equal("{Binding ViewModel, RelativeSource={RelativeSource Self}}", (string?)root.Attribute("DataContext"));
        Assert.Equal("{Binding Server, UpdateSourceTrigger=PropertyChanged}", Attribute(root, "ServerTextBox", "Text"));
        Assert.Equal("{Binding PortCaption, Mode=OneWay}", Attribute(root, "PortLabel", "Content"));
        Assert.Equal("{Binding Level, Mode=OneWay}", Attribute(root, "UploadProgress", "Value"));
        Assert.Equal("Content", Attribute(root, "LogLevelComboBox", "SelectedValuePath"));
        Assert.Equal("{Binding StartDate}", Attribute(root, "StartDatePicker", "SelectedDate"));
        Assert.Null(XDocument.Parse(WpfGenerator.WindowXaml(document, "Demo")).Root!.Attribute("DataContext"));

        // WPF cannot bind a password; its handler copies it.
        var events = WpfGenerator.EventsCode(document, settings, "Demo");
        Assert.Contains("public SettingsViewModel ViewModel { get; } = new();", events);
        Assert.Contains("ViewModel.Secret = SecretPasswordBox.Password;", events);
    }

    [Fact]
    public void WinFormsAddsDataBindings()
    {
        var document = Sample();
        var settings = document.Screens[1];
        var designer = WinFormsGenerator.DesignerCode(document, settings, "Demo");
        Assert.Contains("this.ServerTextBox.DataBindings.Add(\"Text\", this.ViewModel, \"Server\", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged);", designer);
        Assert.Contains("this.UploadProgress.DataBindings.Add(\"Value\", this.ViewModel, \"Level\", true, System.Windows.Forms.DataSourceUpdateMode.Never);", designer);
        Assert.Contains("this.SecureCheckBox.DataBindings.Add(\"Checked\"", designer);

        var events = WinFormsGenerator.EventsCode(document, settings, "Demo");
        Assert.Contains("this.ServersListBox.DataBindings[\"Text\"]?.WriteValue();", events);
        var viewModel = WinFormsGenerator.ViewModel(document, settings, "Demo");
        Assert.Contains("private int _level = 3;", viewModel);
        Assert.Contains("private System.DateTime _startDate = System.DateTime.Today;", viewModel);
    }

    [Fact]
    public void WinUIBindsWithXBind()
    {
        var document = Sample();
        var settings = document.Screens[1];
        var root = XDocument.Parse(WinUIGenerator.WindowXaml(document, settings, "Demo")).Root!;
        Assert.Equal("{x:Bind ViewModel.Server, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", Attribute(root, "ServerTextBox", "Text"));
        Assert.Equal("{x:Bind ViewModel.Secret, Mode=TwoWay}", Attribute(root, "SecretPasswordBox", "Password"));
        Assert.Equal("{x:Bind ViewModel.StartDate, Mode=TwoWay}", Attribute(root, "StartDatePicker", "Date"));
        Assert.Equal("{x:Bind ViewModel.LogLevel, Mode=OneWay}", Attribute(root, "LogLevelComboBox", "SelectedValue"));
        var code = WinUIGenerator.WindowGeneratedCode(document, settings, "Demo");
        Assert.Contains("public SettingsViewModel ViewModel { get; } = new();", code);
        Assert.Contains("ViewModel.LogLevel = (LogLevelComboBox.SelectedItem as ComboBoxItem)?.Content as string;", code);
        var viewModel = WinUIGenerator.ViewModel(document, settings, "Demo");
        Assert.Contains("private bool? _useSecureConnection = true;", viewModel);
        Assert.Contains("private System.DateTimeOffset? _startDate;", viewModel);
    }

    [Fact]
    public void MauiBindsThroughTheBindingContext()
    {
        var document = Sample();
        var files = MauiGenerator.Generate(document, "Demo");
        var page = File(files, "SettingsPage.xaml");
        Assert.Contains("<local:SettingsViewModel />", page);
        Assert.Contains("Text=\"{Binding Server, Mode=TwoWay}\"", page);
        Assert.Contains("IsChecked=\"{Binding UseSecureConnection, Mode=TwoWay}\"", page);
        Assert.Contains("<local:RangeToProgressConverter Minimum=\"0\" Maximum=\"10\" />", page);
        Assert.DoesNotContain("Progress=\"", page);
        Assert.Contains("public SettingsViewModel ViewModel => (SettingsViewModel)BindingContext;", File(files, "SettingsPage.g.cs"));
        Assert.Contains("public sealed class RangeToProgressConverter : IValueConverter", File(files, "RangeToProgressConverter.g.cs"));
        Assert.DoesNotContain("BindingContext", File(files, "MainPage.xaml"));
        XDocument.Parse(page);
    }

    [Fact]
    public void BlazorBindsToTheViewModelInsteadOfFields()
    {
        var document = Sample();
        var files = BlazorGenerator.Generate(document, "Demo");
        var markup = File(files, "Components/Pages/SettingsPage.razor");
        Assert.Contains("@bind=\"ViewModel.Server\"", markup);
        Assert.Contains(">@ViewModel.PortCaption</span>", markup);
        Assert.Contains("value=\"@(ViewModel.Level - (0))\"", markup);

        var events = File(files, "Components/Pages/SettingsPage.Events.g.cs");
        Assert.Contains("public SettingsViewModel ViewModel { get; } = new();", events);
        Assert.Contains("ViewModel.Safe = false;", events);
        Assert.DoesNotContain("private string ServerTextBox", events);
        Assert.Contains("private int DetailsTabs = 0;", events);
        Assert.Contains("namespace Demo.Components.Pages;", File(files, "Components/Pages/SettingsViewModel.g.cs"));
    }

    [Fact]
    public void TheWpfImportReadsBindingsBack()
    {
        var document = Sample();
        var windows = document.Screens.Select(s => new WpfImporter.WindowSource(
            WpfGenerator.ClassName(document, s) + ".xaml",
            WpfGenerator.WindowXaml(document, s, "Demo"),
            WpfGenerator.EventsCode(document, s, "Demo"))).ToList();

        var imported = WpfImporter.Import(windows, "Demo", _ => null).Document;
        string?[] Bindings(ScreenDocument screen) => [.. ControlTree.All(screen.Controls).OrderBy(c => c.Name).Select(c => c.Properties.Binding)];

        // All but the PasswordBox, whose binding is in code.
        var expected = Bindings(document.Screens[1]);
        var actual = Bindings(imported.Screens[1]);
        var names = ControlTree.All(document.Screens[1].Controls).OrderBy(c => c.Name).Select(c => c.Name).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            Assert.Equal(names[i] == "SecretPasswordBox" ? null : expected[i], actual[i]);
        }

        // A bound control's starting value is in the view model, which the import does not read.
        Assert.Equal("", ControlTree.All(imported.Screens[1].Controls).Single(c => c.Name == "PortLabel").Properties.Text);
    }

    [Fact]
    public void TheWpfImportDropsBindingsItCannotKeep()
    {
        const string Xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid Width="400" Height="300">
                <TextBox x:Name="A" Text="{Binding Shared}" Width="100" Height="30" HorizontalAlignment="Left" VerticalAlignment="Top" />
                <CheckBox x:Name="B" IsChecked="{Binding Shared}" Width="100" Height="20" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,40,0,0" />
                <TextBox x:Name="C" Text="{Binding lowerCase}" Width="100" Height="30" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,80,0,0" />
              </Grid>
            </Window>
            """;
        var result = WpfImporter.Import([new WpfImporter.WindowSource("MainWindow.xaml", Xaml, null)], "Demo", _ => null);
        Assert.Equal(1, ControlTree.All(result.Document.MainScreen.Controls).Count(c => c.Properties.Binding == "Shared"));
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("Left out the binding", StringComparison.Ordinal)));
    }
}
