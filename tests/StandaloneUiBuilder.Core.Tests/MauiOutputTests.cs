using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Maui;

namespace StandaloneUiBuilder.Core.Tests;

public class MauiOutputTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace Maui = "http://schemas.microsoft.com/dotnet/2021/maui";

    private static ProjectDocument Sample(string name = "layout-demo") =>
        Unbound.Of(ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj")));

    private static XElement Page(ProjectDocument document, int screen = 0) =>
        XDocument.Parse(MauiGenerator.PageXaml(document, document.Screens[screen], "Demo")).Root!;

    private static XElement Named(XElement root, string name) =>
        root.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void EachScreenIsAPageAndOnlyGeneratedFilesAreRegenerated()
    {
        var files = MauiGenerator.Generate(Sample(), "LayoutDemo").ToDictionary(f => f.RelativePath);

        Assert.Equal(
            ["App.g.cs", "MainPage.g.cs", "MainPage.xaml", "Resources/Images/main_logoimage.png", "SettingsPage.g.cs", "SettingsPage.xaml"],
            files.Values.Where(f => f.Regenerate).Select(f => f.RelativePath).Order());
        Assert.Contains("<UseMaui>true</UseMaui>", files["LayoutDemo.csproj"].Content);
        Assert.Contains("<WindowsPackageType>None</WindowsPackageType>", files["LayoutDemo.csproj"].Content);
        Assert.Contains("new(new MainPage())", files["App.g.cs"].Content);
        Assert.Contains("Width = 816,", files["App.g.cs"].Content);
        Assert.Contains("Height = 640,", files["App.g.cs"].Content);
        Assert.Contains("MauiWinUIApplication", files["Platforms/Windows/App.xaml"].Content);
        var sample = Sample();
        for (var i = 0; i < sample.Screens.Count; i++)
        {
            Assert.Equal(Maui + "ContentPage", Page(sample, i).Name);
        }
    }

    [Fact]
    public void ControlsMapToMauiControlsInTheirDesignedPlaces()
    {
        var main = Page(Sample());
        var settings = Page(Sample(), 1);

        var header = Named(main, "HeaderLabel");
        Assert.Equal(("Fill", "Start", "20,20,20,0", "30"), ((string?)header.Attribute("HorizontalOptions"), (string?)header.Attribute("VerticalOptions"), (string?)header.Attribute("Margin"), (string?)header.Attribute("HeightRequest")));
        Assert.Equal(("Bold", "#1E4E8C", "HeaderLabel"), ((string?)header.Attribute("FontAttributes"), (string?)header.Attribute("TextColor"), (string?)header.Attribute("AutomationId")));
        Assert.Equal("Entry", Named(main, "NameTextBox").Name.LocalName);
        Assert.Equal("VerticalStackLayout", Named(main, "FieldsStack").Name.LocalName);
        Assert.Equal("8", (string?)Named(main, "FieldsStack").Attribute("Spacing"));
        Assert.Equal(("60,*,*", "2*,*"), ((string?)Named(main, "ButtonGrid").Attribute("RowDefinitions"), (string?)Named(main, "ButtonGrid").Attribute("ColumnDefinitions")));
        Assert.Equal("main_logoimage.png", (string?)Named(main, "LogoImage").Attribute("Source"));
        Assert.Equal("Picker", Named(main, "ThemeComboBox").Name.LocalName);

        // A CheckBox sits beside its text in a Grid that takes its place.
        var subscribe = Named(main, "SubscribeCheckBox");
        Assert.Equal("CheckBox", subscribe.Name.LocalName);
        Assert.Equal("Subscribe", (string?)subscribe.Parent!.Elements(Maui + "Label").Single().Attribute("Text"));

        Assert.Equal("Editor", Named(settings, "NotesTextBox").Name.LocalName);
        Assert.Equal("True", (string?)Named(settings, "SecretPasswordBox").Attribute("IsPassword"));
        Assert.Equal("0.3", (string?)Named(settings, "UploadProgress").Attribute("Progress"));
        Assert.Equal(["Primary", "Backup", "Local"], Named(settings, "ServersListBox").Descendants(X + "String").Select(e => e.Value));
        Assert.Equal("8,20,8,8", (string?)Named(settings, "ModeGroup").Elements(Maui + "VerticalStackLayout").Single().Attribute("Margin"));
    }

    [Fact]
    public void GeneratedCodeWiresHooksAndNavigation()
    {
        var document = Sample();
        var main = MauiGenerator.PageGeneratedCode(document, "Demo");
        var settings = MauiGenerator.PageGeneratedCode(document, document.Screens[1], "Demo");

        Assert.Contains("await Navigation.PushModalAsync(new SettingsPage());", main);
        Assert.Contains("await CloseAsync();", settings);
        Assert.Contains("await Navigation.PopModalAsync();", settings);
        Assert.Contains("partial void OnLevelSliderValueChanged(ValueChangedEventArgs e);", settings);

        foreach (var file in MauiGenerator.Generate(document, "Demo").Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var errors = CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, file.RelativePath + ": " + string.Join(Environment.NewLine, errors));
        }
    }

    [Fact]
    public void ASliderRangeIsWrittenInAnOrderMauiAccepts()
    {
        var editor = new DesignEditor();
        var positive = editor.AddControl(ControlType.Slider, 10, 10);
        editor.SetRange(positive.Id, 20, 50, 30);
        var negative = editor.AddControl(ControlType.Slider, 10, 60);
        editor.SetRange(negative.Id, -50, -10, -20);

        var page = MauiGenerator.PageXaml(editor.Document, "T");

        Assert.Contains("Maximum=\"50\" Minimum=\"20\"", page);
        Assert.Contains("Minimum=\"-50\" Maximum=\"-10\"", page);
    }

    [Fact]
    public void NamesThatClashWithThePageAreRefused()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);
        editor.Rename(button.Id, "Navigation");

        Assert.Equal(["\"Navigation\" clashes with a member of the generated page. Rename the control."], MauiGenerator.Check(editor.Document));
        Assert.Empty(MauiGenerator.Check(Sample()));
    }
}
