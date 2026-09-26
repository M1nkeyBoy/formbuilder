using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

public class LibraryOutputTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string File(IReadOnlyList<GeneratedFile> files, string path) => files.Single(f => f.RelativePath == path).Content;

    /// <summary>A WPF-flavoured design with library controls: a Rating with values, and a Badge in a StackPanel.</summary>
    private static ProjectDocument Design(ProjectPlatform platform)
    {
        // WinUI and MAUI have no sample controls: the WPF ones stand in, as their generators only need the names.
        var editor = SampleLibrary.Editor(platform is ProjectPlatform.WinUI or ProjectPlatform.Maui ? ProjectPlatform.Wpf : platform);
        var type = platform switch
        {
            ProjectPlatform.WinForms => "SampleControls.WinForms.Meter",
            ProjectPlatform.Blazor => "SampleControls.Blazor.Banner",
            _ => "SampleControls.Wpf.Rating",
        };

        var control = editor.AddLibraryControl(type, 20, 20)!;
        foreach (var (name, value) in platform switch
        {
            ProjectPlatform.WinForms => new[] { ("Text", "Level \"one\""), ("Level", "3"), ("Direction", "Down"), ("Zoom", "1.5") },
            ProjectPlatform.Blazor => [("Heading", "Hi & @you"), ("Tone", "Loud"), ("Closable", "True")],
            _ => [("Caption", "Rate <us>"), ("Stars", "7"), ("ShowValue", "True"), ("Shape", "Heart"), ("Spacing", "2")],
        })
        {
            Assert.Null(editor.SetLibrarySetting(control.Id, name, value));
        }

        if (platform == ProjectPlatform.Blazor)
        {
            var picker = editor.AddLibraryControl("SampleControls.Blazor.Picker", 20, 100)!;
            Assert.Null(editor.SetLibrarySetting(picker.Id, "TValue", "string"));
            Assert.Null(editor.SetLibrarySetting(picker.Id, "Placeholder", "Pick one"));
        }
        else if (platform != ProjectPlatform.WinForms)
        {
            var stack = editor.AddControl(ControlType.StackPanel, 300, 200);
            editor.AddLibraryControlTo("SampleControls.Wpf.Badge", stack.Id, 310, 210);
        }

        return editor.Document with { Name = "Demo", Platform = platform };
    }

    [Fact]
    public void WpfWritesTheLibraryControlsWithTheirNamespaceAndValues()
    {
        var document = Design(ProjectPlatform.Wpf);
        Assert.Empty(WpfGenerator.Check(document));
        var xaml = WpfGenerator.WindowXaml(document, "Demo");
        Assert.Contains("xmlns:samplecontrols=\"clr-namespace:SampleControls.Wpf;assembly=StandaloneUiBuilder.SampleControls\"", xaml);
        var root = XDocument.Parse(xaml).Root!;
        XNamespace library = "clr-namespace:SampleControls.Wpf;assembly=StandaloneUiBuilder.SampleControls";
        var rating = root.Descendants(library + "Rating").Single();
        Assert.Equal("Rating1", (string?)rating.Attribute(X + "Name"));
        Assert.Equal("Rate <us>", (string?)rating.Attribute("Caption"));
        Assert.Equal(("7", "True", "Heart", "2"), ((string?)rating.Attribute("Stars"), (string?)rating.Attribute("ShowValue"), (string?)rating.Attribute("Shape"), (string?)rating.Attribute("Spacing")));
        Assert.Equal(("Left", "Top", "160", "32"), ((string?)rating.Attribute("HorizontalAlignment"), (string?)rating.Attribute("VerticalAlignment"), (string?)rating.Attribute("Width"), (string?)rating.Attribute("Height")));
        Assert.Equal("StackPanel", root.Descendants(library + "Badge").Single().Parent!.Name.LocalName);
    }

    [Fact]
    public void EveryExportReferencesTheLibraries()
    {
        var document = Design(ProjectPlatform.Wpf);
        var files = WpfGenerator.Generate(document, "Demo");
        var props = XDocument.Parse(File(files, "Directory.Build.props"));
        var reference = props.Descendants("PackageReference").Single();
        Assert.Equal(("Sample.Controls", "1.2.0"), ((string?)reference.Attribute("Include"), (string?)reference.Attribute("Version")));
        Assert.Contains(ProjectExporter.GeneratedMarker, File(files, "Directory.Build.props"));
        Assert.True(files.Single(f => f.RelativePath == "Directory.Build.props").Regenerate);

        // No licence key registered for a vendor the project does not use.
        Assert.DoesNotContain(files, f => f.RelativePath == "LibraryLicenses.g.cs");
        Assert.DoesNotContain(WpfGenerator.Generate(ProjectDocument.CreateBlank(ProjectPlatform.Wpf), "Demo"), f => f.RelativePath == "Directory.Build.props");
    }

    [Fact]
    public void ASyncfusionLicenceKeyIsRegisteredWhenTheAppStarts()
    {
        var document = Design(ProjectPlatform.Wpf);
        document = document with
        {
            Libraries = document.Libraries!.Add(new LibraryPackage { Id = "Syncfusion.SfGrid.WPF", Version = "34.2.9" }),
            LicenseKeys = System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty.Add("Syncfusion", "ABC\"123"),
        };
        var code = File(WpfGenerator.Generate(document, "Demo"), "LibraryLicenses.g.cs");
        Assert.Contains("[System.Runtime.CompilerServices.ModuleInitializer]", code);
        Assert.Contains("Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(\"ABC\\\"123\");", code);
        AssertParses(code);
    }

    [Fact]
    public void WinFormsCreatesTheControlAndSetsItsValues()
    {
        var document = Design(ProjectPlatform.WinForms);
        Assert.Empty(WinFormsGenerator.Check(document));
        var code = WinFormsGenerator.DesignerCode(document, "Demo");
        Assert.Contains("this.Meter1 = new global::SampleControls.WinForms.Meter();", code);
        Assert.Contains("this.Meter1.Text = \"Level \\\"one\\\"\";", code);
        Assert.Contains("this.Meter1.Level = 3;", code);
        Assert.Contains("this.Meter1.Direction = global::SampleControls.WinForms.Direction.Down;", code);
        Assert.Contains("this.Meter1.Zoom = 1.5m;", code);
        Assert.Contains("private global::SampleControls.WinForms.Meter Meter1;", code);
        AssertParses(code);
    }

    [Fact]
    public void WinUIUsesUsingNamespaces()
    {
        var document = Design(ProjectPlatform.WinUI);
        Assert.Empty(WinUIGenerator.Check(document));
        var xaml = WinUIGenerator.WindowXaml(document, "Demo");
        Assert.Contains("xmlns:samplecontrols=\"using:SampleControls.Wpf\"", xaml);
        var rating = XDocument.Parse(xaml).Root!.Descendants((XNamespace)"using:SampleControls.Wpf" + "Rating").Single();
        Assert.Equal("Heart", (string?)rating.Attribute("Shape"));
    }

    [Fact]
    public void MauiNamesTheAssembly()
    {
        var document = Design(ProjectPlatform.Maui);
        Assert.Empty(MauiGenerator.Check(document));
        var xaml = MauiGenerator.PageXaml(document, document.MainScreen, "Demo");
        XNamespace library = "clr-namespace:SampleControls.Wpf;assembly=StandaloneUiBuilder.SampleControls";
        var rating = XDocument.Parse(xaml).Root!.Descendants(library + "Rating").Single();
        Assert.Equal(("Rating1", "7"), ((string?)rating.Attribute("AutomationId"), (string?)rating.Attribute("Stars")));
        Assert.Contains(MauiGenerator.Generate(document, "Demo"), f => f.RelativePath == "Directory.Build.props");
    }

    [Fact]
    public void BlazorWritesComponentsWithTheirParameters()
    {
        var document = Design(ProjectPlatform.Blazor);
        Assert.Empty(BlazorGenerator.Check(document));
        var razor = BlazorGenerator.PageRazor(document, document.MainScreen);
        Assert.Contains("<SampleControls.Blazor.Banner Heading=\"Hi &amp; @@you\" Tone=\"@(global::SampleControls.Blazor.Tone.Loud)\" Closable=\"true\" />", razor);
        Assert.Contains("<SampleControls.Blazor.Picker TValue=\"string\" Placeholder=\"Pick one\" />", razor);
        Assert.Contains("id=\"Banner1\"", razor);
    }

    [Fact]
    public void AControlWhoseLibraryIsGoneCannotBeExported()
    {
        var document = Design(ProjectPlatform.Wpf);
        document = document with { Libraries = [] };
        Assert.Equal(
            ["\"Rating1\" is a Rating from a library the project no longer has. Add the library again (Project > Libraries), or delete the control.",
             "\"Badge1\" is a Badge from a library the project no longer has. Add the library again (Project > Libraries), or delete the control."],
            WpfGenerator.Check(document));
    }

    [Fact]
    public void SyncfusionBlazorAndMauiAreSetUpWhenTheAppStarts()
    {
        var blazor = ProjectDocument.CreateBlank(ProjectPlatform.Blazor) with
        {
            Libraries = [new LibraryPackage { Id = "Syncfusion.Blazor.Buttons", Version = "34.2.9" }],
        };
        var files = BlazorGenerator.Generate(blazor, "Demo");
        Assert.Contains("services.AddSyncfusionBlazor();", File(files, "LibrarySetup.g.cs"));
        Assert.Contains("_content/Syncfusion.Blazor.Themes/fluent2.css", File(files, "Components/LibraryHead.razor"));
        Assert.Contains("syncfusion-blazor.min.js", File(files, "Components/LibraryScripts.razor"));
        Assert.Contains("<PackageReference Include=\"Syncfusion.Blazor.Themes\" Version=\"34.2.9\" />", File(files, "Directory.Build.props"));
        Assert.Contains("builder.Services.AddLibraries();", File(files, "Program.cs"));
        Assert.Contains("<LibraryHead />", File(files, "Components/App.razor"));
        AssertParses(File(files, "LibrarySetup.g.cs"));

        // Without libraries, the setup does nothing, so every export can call it.
        var plain = BlazorGenerator.Generate(ProjectDocument.CreateBlank(ProjectPlatform.Blazor), "Demo");
        Assert.DoesNotContain("Syncfusion", File(plain, "LibrarySetup.g.cs"));
        Assert.DoesNotContain("<link", File(plain, "Components/LibraryHead.razor"));

        var maui = ProjectDocument.CreateBlank(ProjectPlatform.Maui) with
        {
            Libraries = [new LibraryPackage { Id = "Syncfusion.Maui.Buttons", Version = "34.2.9" }],
        };
        var mauiFiles = MauiGenerator.Generate(maui, "Demo");
        Assert.Contains("builder.ConfigureSyncfusionCore();", File(mauiFiles, "LibrarySetup.g.cs"));
        Assert.Contains("builder.UseLibraries();", File(mauiFiles, "MauiProgram.cs"));
        AssertParses(File(mauiFiles, "LibrarySetup.g.cs"));
    }

    private static void AssertParses(string code)
    {
        var errors = CSharpSyntaxTree.ParseText(code).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
