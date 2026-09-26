using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Blazor;

namespace StandaloneUiBuilder.Core.Tests;

public class BlazorOutputTests
{
    private static ProjectDocument Sample(string name = "layout-demo") =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj"));

    private static ProjectDocument Single(ControlDocument control) =>
        ProjectDocument.CreateBlank() with { Name = "Test", Screens = [new ScreenDocument { Controls = [control] }] };

    private static ControlDocument Button(AnchorEdges anchor, string text = "Go") => new()
    {
        Id = Guid.NewGuid(),
        Type = ControlType.Button,
        Name = "GoButton",
        X = 100,
        Y = 50,
        Width = 120,
        Height = 30,
        Anchor = anchor,
        Properties = new ControlProperties { Text = text },
    };

    [Fact]
    public void EachScreenIsAPageAndOnlyGeneratedFilesAreRegenerated()
    {
        var files = BlazorGenerator.Generate(Sample(), "LayoutDemo").ToDictionary(f => f.RelativePath);

        Assert.Equal(
            ["Components/Pages/MainPage.Events.g.cs", "Components/Pages/MainPage.razor", "Components/Pages/SettingsPage.Events.g.cs", "Components/Pages/SettingsPage.razor", "wwwroot/Assets/Main/LogoImage.png", "wwwroot/uib.css"],
            files.Values.Where(f => f.Regenerate).Select(f => f.RelativePath).Order());
        Assert.Contains("LayoutDemo.csproj", files.Keys);
        Assert.Contains("Program.cs", files.Keys);
        Assert.False(files["Components/Pages/SettingsPage.razor.cs"].Regenerate);
        Assert.StartsWith("@page \"/\"", files["Components/Pages/MainPage.razor"].Content);
        Assert.StartsWith("@page \"/settings\"", files["Components/Pages/SettingsPage.razor"].Content);
        Assert.Contains("<PageTitle>Layout demo</PageTitle>", files["Components/Pages/MainPage.razor"].Content);
        Assert.Contains("<PageTitle>Settings</PageTitle>", files["Components/Pages/SettingsPage.razor"].Content);
    }

    [Theory]
    [InlineData(AnchorEdges.Left | AnchorEdges.Top, "position:absolute;left:100px;width:120px;top:50px;height:30px")]
    [InlineData(AnchorEdges.Right | AnchorEdges.Bottom, "position:absolute;right:580px;width:120px;bottom:520px;height:30px")]
    [InlineData(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top, "position:absolute;left:100px;right:580px;top:50px;height:30px")]
    public void ControlsOnTheScreenArePlacedByTheirAnchors(AnchorEdges anchor, string style)
    {
        var page = BlazorGenerator.PageRazor(Single(Button(anchor)));

        Assert.Contains($"<button id=\"GoButton\" style=\"{style}\" type=\"button\" class=\"uib-button\" @onclick=\"GoButton_Click\">Go</button>", page);
    }

    [Fact]
    public void AScreenWithoutRightOrBottomAnchorsKeepsItsSize()
    {
        Assert.Contains("<div class=\"uib-screen\" style=\"width:800px;height:600px\">", BlazorGenerator.PageRazor(Single(Button(AnchorEdges.Default))));
        Assert.Contains("<div class=\"uib-screen\" style=\"width:100%;height:100vh;min-width:800px;min-height:600px\">",
            BlazorGenerator.PageRazor(Single(Button(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top))));
    }

    [Fact]
    public void TextIsEscapedForRazorAndHtml()
    {
        var page = BlazorGenerator.PageRazor(Single(Button(AnchorEdges.Default, "Mail <me> @ \"home\" & *@")) with { Name = "Odd *@ name" });

        Assert.Contains(">Mail &lt;me&gt; @@ &quot;home&quot; &amp; *@@</button>", page);
        Assert.Contains("from \"Odd * @ name\"", page);
    }

    [Fact]
    public void ContainersBecomeFlexBoxesAndCssGrids()
    {
        var page = BlazorGenerator.PageRazor(Sample());

        Assert.Contains("id=\"FieldsStack\" style=\"position:absolute;left:20px;width:250px;top:70px;bottom:230px;flex-direction:column\" class=\"uib-stack\"", page);
        Assert.Contains("grid-template-rows:60px 1fr 1fr;grid-template-columns:2fr 1fr\" class=\"uib-grid\"", page);
        Assert.Contains("id=\"OneButton\" style=\"grid-row:1 / span 1;grid-column:1 / span 2\"", page);
        Assert.Contains("id=\"NotesLabel\" style=\"height:20px;width:100%;margin-top:8px\"", page);
    }

    [Fact]
    public void AGroupBoxHoldsItsChildrenAtTheFixedInset()
    {
        var document = Sample();
        var page = BlazorGenerator.PageRazor(document, document.Screens[1]);

        Assert.Contains("<span class=\"uib-title\" style=\"font-weight:bold\">Mode</span>", page);
        Assert.Contains("<div class=\"uib-content\" style=\"left:8px;top:20px;right:8px;bottom:8px;flex-direction:column\">", page);
        Assert.Contains("<input type=\"radio\" tabindex=\"11\" name=\"ModeGroup\" checked=\"@FastRadio\" @onchange=\"FastRadio_Click\" />", page);
        Assert.Contains("type=\"range\" class=\"uib-range\" min=\"0\" max=\"10\" step=\"1\" @bind=\"LevelSlider\"", page);
        Assert.Contains("max=\"100\" value=\"@(UploadProgress - (0))\"></progress>", page);
        Assert.Contains("<textarea id=\"NotesTextBox\"", page);
        Assert.Contains("type=\"password\"", page);
        Assert.Contains("type=\"date\"", page);
        Assert.Contains("size=\"3\"", page);
    }

    [Fact]
    public void EachControlHasAValueFieldAndAHook()
    {
        var document = Sample();
        var code = BlazorGenerator.EventsCode(document, document.Screens[1], "LayoutDemo");

        Assert.Contains("namespace LayoutDemo.Components.Pages;", code);
        Assert.Contains("private string ServerTextBox = \"localhost\";", code);
        Assert.Contains("private bool FastRadio = true;", code);
        Assert.Contains("private int LevelSlider = 3;", code);
        Assert.Contains("private DateOnly? StartDatePicker = null;", code);
        Assert.Contains("partial void OnLevelSliderValueChanged();", code);

        // Choosing a radio button clears the rest of its group, then runs its hook.
        Assert.Contains("""
                private void SafeRadio_Click()
                {
                    SafeRadio = true;
                    FastRadio = false;
                    OnSafeRadioClick();
                }
            """.ReplaceLineEndings(), code.ReplaceLineEndings());
    }

    [Fact]
    public void ButtonActionsNavigate()
    {
        var document = Sample();

        Assert.Contains("Navigation.NavigateTo(\"/settings\");", BlazorGenerator.EventsCode(document, "LayoutDemo"));
        Assert.Contains("_ = JS.InvokeVoidAsync(\"history.back\");", BlazorGenerator.EventsCode(document, document.Screens[1], "LayoutDemo"));
    }

    [Fact]
    public void GeneratedCodeIsValidCSharp()
    {
        foreach (var file in BlazorGenerator.Generate(Sample(), "LayoutDemo").Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var errors = CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, file.RelativePath + ": " + string.Join(Environment.NewLine, errors));
        }
    }

    [Fact]
    public void NamesThatClashWithThePageAreRefused()
    {
        var document = Single(Button(AnchorEdges.Default) with { Name = "Navigation" });

        Assert.Equal(["\"Navigation\" clashes with a member of the generated page. Rename the control."], BlazorGenerator.Check(document));
        Assert.Empty(BlazorGenerator.Check(Sample()));
    }

    [Fact]
    public void ExportingAgainKeepsTheFirstNamespaceAndTheDevelopersCode()
    {
        var parent = Directory.CreateTempSubdirectory("uib-blazor-").FullName;
        try
        {
            var first = BlazorExporter.Export(Sample() with { Name = "Layout demo" }, parent);
            var codeFile = Path.Combine(first.ProjectFolder, "Components", "Pages", "MainPage.razor.cs");
            File.AppendAllText(codeFile, "// mine\n");

            // Same folder (the renamed project is exported into the same place by path).
            var again = BlazorExporter.Export(Sample() with { Name = "Layout demo" }, parent);

            Assert.Empty(again.Created);
            Assert.EndsWith("// mine\n", File.ReadAllText(codeFile).ReplaceLineEndings("\n"));
            Assert.Contains("namespace LayoutDemo.Components.Pages;", File.ReadAllText(Path.Combine(first.ProjectFolder, "Components", "Pages", "MainPage.Events.g.cs")));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
