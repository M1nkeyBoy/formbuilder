using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinUI;

namespace StandaloneUiBuilder.Core.Tests;

public class WinUIOutputTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static ProjectDocument Sample(string name = "layout-demo") =>
        Unbound.Of(ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj")));

    private static XElement Window(ProjectDocument document, int screen = 0) =>
        XDocument.Parse(WinUIGenerator.WindowXaml(document, document.Screens[screen], "Demo")).Root!;

    private static XElement Named(XElement root, string name) =>
        root.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void EachScreenIsAWindowAndOnlyGeneratedFilesAreRegenerated()
    {
        var files = WinUIGenerator.Generate(Sample(), "LayoutDemo").ToDictionary(f => f.RelativePath);

        Assert.Equal(
            ["Assets/Main/LogoImage.png", "MainWindow.g.cs", "MainWindow.xaml", "SettingsWindow.g.cs", "SettingsWindow.xaml"],
            files.Values.Where(f => f.Regenerate).Select(f => f.RelativePath).Order());
        Assert.Contains("<UseWinUI>true</UseWinUI>", files["LayoutDemo.csproj"].Content);
        Assert.Contains("<WindowsPackageType>None</WindowsPackageType>", files["LayoutDemo.csproj"].Content);
        Assert.Contains("<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>", files["LayoutDemo.csproj"].Content);
        Assert.Contains($"Include=\"Microsoft.WindowsAppSDK\" Version=\"{WinUIGenerator.WindowsAppSdkVersion}\"", files["LayoutDemo.csproj"].Content);
        Assert.Contains("window = new MainWindow();", files["App.xaml.cs"].Content);
        Assert.Contains("<XamlControlsResources", files["App.xaml"].Content);
        Assert.Contains("PerMonitorV2", files["app.manifest"].Content);
        Assert.Contains("InitializeWindow();", files["SettingsWindow.xaml.cs"].Content);
    }

    [Fact]
    public void EveryWindowIsWellFormedXaml()
    {
        var document = Sample();
        foreach (var screen in document.Screens)
        {
            var root = XDocument.Parse(WinUIGenerator.WindowXaml(document, screen, "Demo")).Root!;
            Assert.Equal(Ui + "Window", root.Name);
            Assert.Equal($"Demo.{WinUIGenerator.ClassName(document, screen)}", (string?)root.Attribute(X + "Class"));
        }
    }

    [Fact]
    public void ControlsKeepTheirDesignedSizeAndPlacement()
    {
        var root = Window(Sample());

        var name = Named(root, "NameTextBox");
        Assert.Equal(("0", "0"), ((string?)name.Attribute("MinWidth"), (string?)name.Attribute("MinHeight")));
        Assert.Equal("30", (string?)name.Attribute("Height"));

        var header = Named(root, "HeaderLabel");
        Assert.Equal("ContentControl", header.Name.LocalName);
        Assert.Equal(("Stretch", "Top", "20,20,20,0"), ((string?)header.Attribute("HorizontalAlignment"), (string?)header.Attribute("VerticalAlignment"), (string?)header.Attribute("Margin")));
        Assert.Equal(("16", "Bold", "#1E4E8C"), ((string?)header.Attribute("FontSize"), (string?)header.Attribute("FontWeight"), (string?)header.Attribute("Foreground")));

        Assert.Equal("ms-appx:///Assets/Main/LogoImage.png", (string?)Named(root, "LogoImage").Attribute("Source"));
        Assert.Equal("RowDefinition", Named(root, "ButtonGrid").Descendants().First(e => e.Name.LocalName == "RowDefinition").Name.LocalName);
    }

    [Fact]
    public void AGroupBoxIsDrawnAroundAStackAtTheFixedInset()
    {
        var root = Window(Sample(), 1);

        var group = Named(root, "ModeGroup");
        Assert.Equal("Grid", group.Name.LocalName);
        Assert.Equal("Mode", (string?)group.Descendants(Ui + "TextBlock").Single().Attribute("Text"));
        var stack = group.Elements(Ui + "StackPanel").Single();
        Assert.Equal("8,20,8,8", (string?)stack.Attribute("Margin"));
        Assert.Equal(["FastRadio", "SafeRadio", "LevelSlider", "UploadProgress"], stack.Elements().Select(e => (string?)e.Attribute(X + "Name")));
        Assert.Equal("CalendarDatePicker", Named(root, "StartDatePicker").Name.LocalName);
        Assert.Equal("1", (string?)Named(root, "LevelSlider").Attribute("StepFrequency"));
    }

    [Fact]
    public void GeneratedCodeSizesTheWindowAndWiresHooksAndActions()
    {
        var document = Sample();
        var main = WinUIGenerator.WindowGeneratedCode(document, "Demo");
        var settings = WinUIGenerator.WindowGeneratedCode(document, document.Screens[1], "Demo");

        Assert.Contains("Title = \"Layout demo\";", main);
        Assert.Contains("AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(800 * scale), (int)Math.Round(600 * scale)));", main);
        Assert.DoesNotContain("IsResizable = false", main);
        Assert.Contains("new SettingsWindow().Activate();", main);
        Assert.Contains("Close();", settings);
        Assert.Contains("private void StartDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e) => OnStartDatePickerDateChanged(e);", settings);
        Assert.Contains("partial void OnLevelSliderValueChanged(RangeBaseValueChangedEventArgs e);", settings);

        foreach (var file in WinUIGenerator.Generate(document, "Demo").Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var errors = CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, file.RelativePath + ": " + string.Join(Environment.NewLine, errors));
        }
    }

    [Fact]
    public void AFixedSizeScreenCannotBeResized()
    {
        var code = WinUIGenerator.WindowGeneratedCode(Sample("customer-form"), "Demo");
        var customer = Sample("customer-form");

        Assert.Equal(AnchorLayout.IsResizable(customer.MainScreen), !code.Contains("presenter.IsResizable = false;", StringComparison.Ordinal));
    }

    [Fact]
    public void NamesThatClashWithTheWindowAreRefused()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);
        editor.Rename(button.Id, "AppWindow");

        Assert.Equal(["\"AppWindow\" clashes with a member of the generated window. Rename the control."], WinUIGenerator.Check(editor.Document));
        Assert.Empty(WinUIGenerator.Check(Sample()));
    }
}
