using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Import.Wpf;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;

namespace StandaloneUiBuilder.Core.Tests;

public class ProjectStyleTests
{
    private const string Minimal = """{ "schemaVersion": 19, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screens": [{ "id": "main", "name": "Main" }] }""";

    private static ProjectDocument Sample(ProjectTheme theme, ProjectStyle style) =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj")) with { Theme = theme, Style = style };

    private static string Css(ProjectDocument document) =>
        BlazorGenerator.Generate(document, "Demo").Single(f => f.RelativePath == "wwwroot/uib.css").Content.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void NewProjectsAreModernAndOlderOnesKeepTheClassicLook()
    {
        Assert.Equal(ProjectStyle.Modern, ProjectDocument.CreateBlank().Style);
        Assert.Equal(ProjectStyle.Modern, new DesignEditor().Document.Style);
        Assert.Equal(ProjectStyle.Classic, ProjectFile.Deserialize(Minimal).Style);
    }

    [Fact]
    public void TheStyleIsSavedOnlyWhenModern()
    {
        var modern = ProjectFile.Serialize(ProjectDocument.CreateBlank());
        Assert.Contains("\"style\": \"Modern\"", modern);
        Assert.Equal(ProjectStyle.Modern, ProjectFile.Deserialize(modern).Style);

        var classic = ProjectFile.Serialize(ProjectDocument.CreateBlank() with { Style = ProjectStyle.Classic });
        Assert.DoesNotContain("\"style\"", classic);
        Assert.Equal(ProjectStyle.Classic, ProjectFile.Deserialize(classic).Style);
    }

    [Fact]
    public void ChangingTheStyleCanBeUndone()
    {
        var editor = new DesignEditor();

        Assert.True(editor.SetStyle(ProjectStyle.Classic));
        Assert.False(editor.SetStyle(ProjectStyle.Classic));
        Assert.Equal(ProjectStyle.Classic, editor.Document.Style);

        editor.Undo();
        Assert.Equal(ProjectStyle.Modern, editor.Document.Style);
    }

    [Theory]
    [InlineData(ProjectTheme.Light, ProjectStyle.Classic, false)]
    [InlineData(ProjectTheme.Light, ProjectStyle.Modern, true)]
    [InlineData(ProjectTheme.Dark, ProjectStyle.Classic, true)]
    [InlineData(ProjectTheme.System, ProjectStyle.Classic, true)]
    public void WpfUsesFluentWhenModernOrNotLight(ProjectTheme theme, ProjectStyle style, bool fluent) =>
        Assert.Equal(fluent, (ProjectDocument.CreateBlank() with { Theme = theme, Style = style }).UsesFluent);

    [Fact]
    public void ModernBlazorControlsAreRoundedAndFollowTheTheme()
    {
        var classic = Css(Sample(ProjectTheme.Light, ProjectStyle.Classic));
        Assert.DoesNotContain("border-radius: 4px", classic);
        Assert.DoesNotContain("accent-color", classic);

        var modern = Css(Sample(ProjectTheme.Light, ProjectStyle.Modern));
        Assert.Contains(".uib-input, .uib-button { border: 1px solid var(--uib-control-line); border-radius: 4px;", modern);
        Assert.Contains("accent-color: var(--uib-accent)", modern);

        // The dark colours come after the light ones, so they win when the page is dark.
        var dark = Css(Sample(ProjectTheme.Dark, ProjectStyle.Modern));
        Assert.True(dark.IndexOf("--uib-control: #fff", StringComparison.Ordinal) < dark.IndexOf("--uib-control: #2d2d2d", StringComparison.Ordinal));
        Assert.Contains("@media (prefers-color-scheme: dark) { :root { color-scheme: dark;", Css(Sample(ProjectTheme.System, ProjectStyle.Modern)));
    }

    [Theory]
    [InlineData(null, ProjectStyle.Classic)]
    [InlineData("Light", ProjectStyle.Modern)]
    [InlineData("Dark", ProjectStyle.Modern)]
    public void ImportedWindowsAreModernWhenTheyUseFluent(string? themeMode, ProjectStyle expected)
    {
        var mode = themeMode is null ? "" : $" ThemeMode=\"{themeMode}\"";
        var xaml = $"""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Order" Width="400" Height="300"{mode}>
                <Grid><Button Content="OK" Width="80" Height="30" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="10,10,0,0" /></Grid>
            </Window>
            """;

        var (document, _) = WpfImporter.Import([new WpfImporter.WindowSource("MainWindow.xaml", xaml, null)], "Order", _ => null);

        Assert.Equal(expected, document.Style);
    }
}
