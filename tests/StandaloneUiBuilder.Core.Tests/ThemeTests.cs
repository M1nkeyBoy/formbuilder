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

public class ThemeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static ProjectDocument Sample(ProjectTheme theme) =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj")) with { Theme = theme };

    private static string File(IReadOnlyList<GeneratedFile> files, string path) =>
        files.Single(f => f.RelativePath == path).Content;

    [Fact]
    public void ANewProjectIsLightAndTheFileOnlyStoresOtherThemes()
    {
        var document = ProjectDocument.CreateBlank();
        Assert.Equal(ProjectTheme.Light, document.Theme);
        Assert.DoesNotContain("\"theme\"", ProjectFile.Serialize(document));

        var json = ProjectFile.Serialize(document with { Theme = ProjectTheme.Dark });
        Assert.Contains("\"theme\": \"Dark\"", json);
        Assert.Equal(ProjectTheme.Dark, ProjectFile.Deserialize(json).Theme);
        Assert.Equal(ProjectTheme.System, ProjectFile.Deserialize(ProjectFile.Serialize(document with { Theme = ProjectTheme.System })).Theme);
    }

    [Fact]
    public void AnUnknownThemeIsRejected()
    {
        var json = ProjectFile.Serialize(ProjectDocument.CreateBlank() with { Theme = ProjectTheme.Dark }).Replace("\"Dark\"", "\"Purple\"", StringComparison.Ordinal);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));
    }

    [Fact]
    public void ChoosingAThemeIsOneUndoStep()
    {
        var editor = new DesignEditor();
        Assert.False(editor.SetTheme(ProjectTheme.Light));
        Assert.False(editor.IsDirty);

        Assert.True(editor.SetTheme(ProjectTheme.Dark));
        Assert.Equal(ProjectTheme.Dark, editor.Document.Theme);
        Assert.True(editor.IsDirty);

        editor.Undo();
        Assert.Equal(ProjectTheme.Light, editor.Document.Theme);
        editor.Redo();
        Assert.Equal(ProjectTheme.Dark, editor.Document.Theme);
    }

    [Fact]
    public void WpfWindowsUseTheFluentThemeWithTheDesignSizes()
    {
        var light = XDocument.Parse(WpfGenerator.WindowXaml(Sample(ProjectTheme.Light), "Demo")).Root!;
        Assert.Null(light.Attribute("ThemeMode"));
        Assert.DoesNotContain(light.Descendants(), e => e.Attribute("MinHeight") is { Value: "0" });

        foreach (var (theme, mode) in new[] { (ProjectTheme.Dark, "Dark"), (ProjectTheme.System, "System") })
        {
            var document = Sample(theme);
            foreach (var screen in document.Screens)
            {
                var root = XDocument.Parse(WpfGenerator.WindowXaml(document, screen, "Demo")).Root!;
                Assert.Equal(mode, (string?)root.Attribute("ThemeMode"));
            }

            // Fluent's minimum sizes (a text box is at least 32 high) are reset on every control.
            var main = XDocument.Parse(WpfGenerator.WindowXaml(document, "Demo")).Root!;
            var name = main.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "NameTextBox");
            Assert.Equal("0", (string?)name.Attribute("MinHeight"));
            Assert.Equal("0", (string?)name.Attribute("MinWidth"));
        }
    }

    [Fact]
    public void WinFormsSetsTheColourModeBeforeTheFirstForm()
    {
        var light = Sample(ProjectTheme.Light);
        Assert.DoesNotContain("SetColorMode", WinFormsGenerator.DesignerCode(light, "Demo"));

        var dark = Sample(ProjectTheme.Dark);
        Assert.Contains("static MainForm()", WinFormsGenerator.DesignerCode(dark, "Demo"));
        Assert.Contains("System.Windows.Forms.Application.SetColorMode(System.Windows.Forms.SystemColorMode.Dark);", WinFormsGenerator.DesignerCode(dark, "Demo"));
        Assert.DoesNotContain("SetColorMode", WinFormsGenerator.DesignerCode(dark, dark.Screens[1], "Demo"));

        Assert.Contains("SystemColorMode.System);", WinFormsGenerator.DesignerCode(Sample(ProjectTheme.System), "Demo"));
    }

    [Theory]
    [InlineData(ProjectTheme.Light, "Light")]
    [InlineData(ProjectTheme.Dark, "Dark")]
    [InlineData(ProjectTheme.System, null)]
    public void WinUIWindowsRequestTheTheme(ProjectTheme theme, string? requested)
    {
        var document = Sample(theme);
        foreach (var screen in document.Screens)
        {
            var root = XDocument.Parse(WinUIGenerator.WindowXaml(document, screen, "Demo")).Root!;
            Assert.Equal(requested, (string?)root.Elements().First().Attribute("RequestedTheme"));
        }
    }

    [Theory]
    [InlineData(ProjectTheme.Light, "Light")]
    [InlineData(ProjectTheme.Dark, "Dark")]
    [InlineData(ProjectTheme.System, "Unspecified")]
    public void MauiAppsSetTheirTheme(ProjectTheme theme, string appTheme)
    {
        var files = MauiGenerator.Generate(Sample(theme), "Demo");
        Assert.Contains($"Current!.UserAppTheme = AppTheme.{appTheme};", File(files, "App.g.cs"));

        // The drawn tabs follow the theme.
        Assert.Contains("BackgroundColor=\"{AppThemeBinding Light=#FFFFFF, Dark=#2B2B2B}\"", File(files, "SettingsPage.xaml"));
        Assert.Contains("SetAppThemeColor(VisualElement.BackgroundColorProperty", File(files, "SettingsPage.g.cs"));
    }

    [Fact]
    public void BlazorStylesUseTheThemesColours()
    {
        var light = File(BlazorGenerator.Generate(Sample(ProjectTheme.Light), "Demo"), "wwwroot/uib.css");
        Assert.Contains(":root { color-scheme: light;", light);
        Assert.DoesNotContain("color-scheme: dark", light);
        Assert.Contains("border: 1px solid var(--uib-line)", light);

        var dark = File(BlazorGenerator.Generate(Sample(ProjectTheme.Dark), "Demo"), "wwwroot/uib.css");
        Assert.Contains("\n:root { color-scheme: dark; --uib-page: #202020;", dark.Replace("\r\n", "\n", StringComparison.Ordinal));

        var system = File(BlazorGenerator.Generate(Sample(ProjectTheme.System), "Demo"), "wwwroot/uib.css");
        Assert.Contains("@media (prefers-color-scheme: dark) { :root { color-scheme: dark;", system);
    }

    [Fact]
    public void TextOnTheDesignsOwnBackgroundsContrastsWithThem()
    {
        var settings = Sample(ProjectTheme.Dark).Screens[1];
        ControlDocument Find(ScreenDocument screen, string name) => ControlTree.All(screen.Controls).Single(c => c.Name == name);

        // Unchanged in the light theme.
        Assert.Same(settings, ThemeContrast.Apply(ProjectTheme.Light, settings));

        var dark = ThemeContrast.Apply(ProjectTheme.Dark, settings);
        Assert.Equal("#F3F6FA", Find(dark, "SettingsGrid").Properties.Background);
        Assert.Null(Find(dark, "SettingsGrid").Properties.Foreground);

        // Labels and check boxes in the light grid get black text; text boxes draw their own background.
        Assert.Equal("#000000", Find(dark, "ServerLabel").Properties.Foreground);
        Assert.Equal("#000000", Find(dark, "SecureCheckBox").Properties.Foreground);
        Assert.Null(Find(dark, "ServerTextBox").Properties.Foreground);

        // A control's own background counts, and a colour of its own stands.
        Assert.Equal(Find(settings, "SettingsLabel").Properties.Foreground, Find(dark, "SettingsLabel").Properties.Foreground);
        Assert.Equal("#000000", ThemeContrast.ContrastingText("#F3F6FA"));
        Assert.Equal("#FFFFFF", ThemeContrast.ContrastingText("#1E6FD9"));
        Assert.Equal("#FFFFFF", ThemeContrast.ContrastingText("#202020"));

        // Every output shows it.
        var document = Sample(ProjectTheme.Dark);
        var wpf = XDocument.Parse(WpfGenerator.WindowXaml(document, document.Screens[1], "Demo")).Root!;
        Assert.Equal("#000000", (string?)wpf.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "ServerLabel").Attribute("Foreground"));
        Assert.Contains("color:#000000", BlazorGenerator.PageRazor(document, document.Screens[1]));
    }

    [Theory]
    [InlineData(ProjectTheme.Light)]
    [InlineData(ProjectTheme.Dark)]
    [InlineData(ProjectTheme.System)]
    public void TheWpfImportReadsTheThemeBack(ProjectTheme theme)
    {
        var document = Sample(theme);
        var windows = document.Screens.Select(s => new WpfImporter.WindowSource(
            WpfGenerator.ClassName(document, s) + ".xaml",
            WpfGenerator.WindowXaml(document, s, "Demo"),
            WpfGenerator.EventsCode(document, s, "Demo"))).ToList();

        var result = WpfImporter.Import(windows, "Demo", _ => null);
        Assert.Equal(theme, result.Document.Theme);
    }
}
