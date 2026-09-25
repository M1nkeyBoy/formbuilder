using System.Xml.Linq;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

public sealed class WpfOutputTests : IDisposable
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly string directory = Directory.CreateTempSubdirectory("uib-wpf-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "customer-form.uibproj"));

    private static XElement Canvas(ProjectDocument document)
    {
        var window = XDocument.Parse(WpfGenerator.WindowXaml(document, "Test")).Root!;
        return window.Element(Presentation + "Canvas")!;
    }

    private static ProjectDocument WithControl(ControlType type, string name, ControlProperties properties)
    {
        var editor = new DesignEditor();
        var control = editor.AddControl(type, 0, 0);
        editor.Rename(control.Id, name);
        var document = editor.Document;
        var renamed = document.Screen.Controls[0] with { Properties = properties };
        return document with { Screen = document.Screen with { Controls = [renamed] } };
    }

    [Theory]
    [InlineData("Customer form", "CustomerForm")]
    [InlineData("customer-form", "CustomerForm")]
    [InlineData("my  app 2", "MyApp2")]
    [InlineData("2024 plan", "App2024Plan")]
    [InlineData("---", "GeneratedApp")]
    public void ProjectNameBecomesANamespace(string name, string expected)
    {
        Assert.Equal(expected, WpfGenerator.ToNamespace(name));
    }

    [Fact]
    public void WindowHasEveryControlInDrawOrderWithItsGeometry()
    {
        var document = Sample();

        var elements = Canvas(document).Elements().ToList();

        Assert.Equal(document.Screen.Controls.Count, elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var control = document.Screen.Controls[i];
            var element = elements[i];
            Assert.Equal(control.Type.ToString(), element.Name.LocalName);
            Assert.Equal(control.Name, (string?)element.Attribute(Xaml + "Name"));
            Assert.Equal(control.X.ToString(), (string?)element.Attribute("Canvas.Left"));
            Assert.Equal(control.Y.ToString(), (string?)element.Attribute("Canvas.Top"));
            Assert.Equal(control.Width.ToString(), (string?)element.Attribute("Width"));
            Assert.Equal(control.Height.ToString(), (string?)element.Attribute("Height"));
        }
    }

    [Fact]
    public void WindowIsSizedToTheScreenAndTitledAfterTheProject()
    {
        var root = XDocument.Parse(WpfGenerator.WindowXaml(Sample(), "CustomerForm")).Root!;
        var canvas = root.Element(Presentation + "Canvas")!;

        Assert.Equal("CustomerForm.MainWindow", (string?)root.Attribute(Xaml + "Class"));
        Assert.Equal("Customer form", (string?)root.Attribute("Title"));
        Assert.Equal("WidthAndHeight", (string?)root.Attribute("SizeToContent"));
        Assert.Equal("800", (string?)canvas.Attribute("Width"));
        Assert.Equal("600", (string?)canvas.Attribute("Height"));
    }

    [Fact]
    public void TypeSpecificPropertiesAreWritten()
    {
        var elements = Canvas(Sample()).Elements().ToDictionary(e => (string)e.Attribute(Xaml + "Name")!);

        Assert.Equal("Customer details", (string?)elements["TitleLabel"].Attribute("Content"));
        Assert.Equal("Submit", (string?)elements["SubmitButton"].Attribute("Content"));
        Assert.Equal("", (string?)elements["NameTextBox"].Attribute("Text"));
        Assert.Equal("True", (string?)elements["NewsletterCheckBox"].Attribute("IsChecked"));
        Assert.Equal("Send me the newsletter", (string?)elements["NewsletterCheckBox"].Attribute("Content"));
        Assert.Equal(
            ["Basic", "Standard", "Premium"],
            elements["PlanComboBox"].Elements(Presentation + "ComboBoxItem").Select(i => (string?)i.Attribute("Content")));
    }

    [Fact]
    public void SpecialCharactersAreEscapedAndUnderscoresShowLiterally()
    {
        var document = WithControl(ControlType.Button, "Tricky", new ControlProperties { Text = "Save_As \"<&>\"\n{Binding}" });

        var content = (string?)Canvas(document).Elements().Single().Attribute("Content");

        Assert.Equal("Save__As \"<&>\"\n{Binding}", content);
        Assert.Contains("Content=\"Save__As &quot;&lt;&amp;&gt;&quot;&#10;{Binding}\"", WpfGenerator.WindowXaml(document, "Test"));
    }

    [Fact]
    public void LeadingBraceIsNotReadAsAMarkupExtension()
    {
        var document = WithControl(ControlType.TextBox, "Box", new ControlProperties { Text = "{Binding Name}" });

        Assert.Contains("Text=\"{}{Binding Name}\"", WpfGenerator.WindowXaml(document, "Test"));
    }

    [Fact]
    public void TextBoxUnderscoresAreNotDoubled()
    {
        var document = WithControl(ControlType.TextBox, "Box", new ControlProperties { Text = "a_b" });

        Assert.Equal("a_b", (string?)Canvas(document).Elements().Single().Attribute("Text"));
    }

    [Fact]
    public void EmptyScreenGivesAnEmptyCanvas()
    {
        Assert.Empty(Canvas(ProjectDocument.CreateBlank()).Elements());
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        Assert.Equal(WpfGenerator.WindowXaml(Sample(), "A"), WpfGenerator.WindowXaml(Sample(), "A"));
    }

    [Theory]
    [InlineData("class")]
    [InlineData("MainWindow")]
    [InlineData("Content")]
    [InlineData("InitializeComponent")]
    public void NamesThatWouldBreakTheGeneratedCodeAreReported(string name)
    {
        var document = WithControl(ControlType.Button, name, new ControlProperties { Text = "x" });

        var problem = Assert.Single(WpfGenerator.Check(document));
        Assert.Contains(name, problem);
        Assert.Throws<WpfExportException>(() => WpfExporter.Export(document, directory));
    }

    [Fact]
    public void ExportCreatesARunnableProjectLayout()
    {
        var result = WpfExporter.Export(Sample(), directory);

        Assert.Equal(Path.Combine(directory, "CustomerForm"), result.ProjectFolder);
        Assert.Equal(
            ["App.xaml", "App.xaml.cs", "CustomerForm.csproj", "MainWindow.xaml", "MainWindow.xaml.cs"],
            Directory.GetFiles(result.ProjectFolder).Select(Path.GetFileName).Order());
        Assert.Equal(5, result.Created.Count);
        Assert.Contains("<UseWPF>true</UseWPF>", File.ReadAllText(Path.Combine(result.ProjectFolder, "CustomerForm.csproj")));
        Assert.Contains("namespace CustomerForm;", File.ReadAllText(Path.Combine(result.ProjectFolder, "MainWindow.xaml.cs")));
    }

    [Fact]
    public void ReExportRegeneratesTheWindowAndKeepsTheDevelopersCode()
    {
        var document = Sample();
        var folder = WpfExporter.Export(document, directory).ProjectFolder;
        var codeBehind = Path.Combine(folder, "MainWindow.xaml.cs");
        File.AppendAllText(codeBehind, "// my code\n");

        var moved = document with
        {
            Screen = document.Screen with { Controls = document.Screen.Controls.SetItem(0, document.Screen.Controls[0] with { X = 70 }) },
        };
        var result = WpfExporter.Export(moved, directory);

        Assert.Equal(["MainWindow.xaml"], result.Updated);
        Assert.Empty(result.Created);
        Assert.EndsWith("// my code\n", File.ReadAllText(codeBehind));
        Assert.Contains("Canvas.Left=\"70\"", File.ReadAllText(Path.Combine(folder, "MainWindow.xaml")));
    }

    [Fact]
    public void ReExportingUnchangedDesignWritesNothing()
    {
        WpfExporter.Export(Sample(), directory);

        var result = WpfExporter.Export(Sample(), directory);

        Assert.Empty(result.Created);
        Assert.Empty(result.Updated);
        Assert.Equal(5, result.Kept.Count);
    }

    [Fact]
    public void ReExportKeepsTheOriginalNamespaceAfterARename()
    {
        var document = Sample();
        var folder = WpfExporter.Export(document, directory).ProjectFolder;

        // Same folder, but the project is now called something else.
        var renamed = document with { Name = "Something else" };
        WpfExporter.Export(renamed, directory);
        var renamedFolder = WpfExporter.ProjectFolderFor(renamed, directory);

        Assert.NotEqual(folder, renamedFolder);
        Assert.Contains("x:Class=\"SomethingElse.MainWindow\"", File.ReadAllText(Path.Combine(renamedFolder, "MainWindow.xaml")));

        // Exporting the renamed project into the original folder keeps that folder's namespace.
        File.Copy(Path.Combine(folder, "MainWindow.xaml.cs"), Path.Combine(renamedFolder, "MainWindow.xaml.cs"), overwrite: true);
        WpfExporter.Export(renamed, directory);
        Assert.Contains("x:Class=\"CustomerForm.MainWindow\"", File.ReadAllText(Path.Combine(renamedFolder, "MainWindow.xaml")));
    }

    [Fact]
    public void AWindowFileNotMadeByTheBuilderIsNeverReplaced()
    {
        var folder = Path.Combine(directory, "CustomerForm");
        Directory.CreateDirectory(folder);
        var handWritten = Path.Combine(folder, "MainWindow.xaml");
        File.WriteAllText(handWritten, "<Window />");

        var ex = Assert.Throws<WpfExportException>(() => WpfExporter.Export(Sample(), directory));

        Assert.Contains("not created by Standalone UI Builder", ex.Message);
        Assert.Equal("<Window />", File.ReadAllText(handWritten));
    }
}
