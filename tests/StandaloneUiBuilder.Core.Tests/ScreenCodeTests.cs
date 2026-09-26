using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>A screen's view model code, written in the builder; the sample's Settings screen implements OnSave.</summary>
public class ScreenCodeTests
{
    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    [Fact]
    public void CodeIsTidiedStoredAndUndone()
    {
        var editor = new DesignEditor();
        Assert.False(editor.SetScreenCode("   \r\n  "));
        Assert.True(editor.SetScreenCode("\r\npartial void OnSave()   \r\n{\r\n}\r\n\r\n"));
        Assert.Equal("partial void OnSave()\n{\n}", editor.Screen.Code);
        Assert.False(editor.SetScreenCode("partial void OnSave()\n{\n}"));
        Assert.True(DataBindings.HasViewModel(editor.Screen));

        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains("\"code\": \"partial void OnSave()\\n{\\n}\"", json);
        Assert.Equal(editor.Screen.Code, ProjectFile.Deserialize(json).MainScreen.Code);

        // A duplicated screen keeps its code.
        Assert.Equal(editor.Screen.Code, editor.DuplicateScreen().Code);
        editor.Undo();
        editor.Undo();
        Assert.Null(editor.Screen.Code);
    }

    [Fact]
    public void TheHooksAreTheCommandsAndChangeMethods()
    {
        var settings = Sample().Screens[1];
        var hooks = DataBindings.Hooks(settings);
        Assert.Equal("OnSave", hooks[0].Name);
        Assert.Equal("Runs when SaveSettingsButton is clicked.", hooks[0].Description);
        Assert.Contains(hooks, h => h.Name == "OnLevelChanged");
        Assert.True(DataBindings.Implements(settings.Code, hooks[0]));
        Assert.False(DataBindings.Implements(settings.Code, hooks.Single(h => h.Name == "OnLevelChanged")));
        Assert.Equal("partial void OnSave()\n{\n}\n", hooks[0].Stub);
    }

    [Fact]
    public void TheCodeIsExportedAsAPartOfTheViewModel()
    {
        var editor = new DesignEditor();
        editor.SetScreenCode("using System.Text;\n\npublic string Summary => new StringBuilder(\"a\").ToString();\n\npartial void OnSave()\n{\n    Summary.ToString();\n}");
        var code = ViewModelCode.UserCode(editor.Document, editor.Screen, "Demo")!;
        Assert.Contains("#nullable enable\nusing System.Text;\n\nnamespace Demo;\n\npublic partial class MainViewModel\n{\n    public string Summary", code.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("\n    {\n        Summary.ToString();\n    }\n}\n", code.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains(ProjectExporter.GeneratedMarker, code);
        Assert.Null(ViewModelCode.UserCode(Sample(), Sample().MainScreen, "Demo"));
    }

    [Fact]
    public void EveryTargetWritesTheCodeBesideTheViewModel()
    {
        var document = Sample();
        static string File(IReadOnlyList<GeneratedFile> files, string path) => files.Single(f => f.RelativePath == path && f.Regenerate).Content;
        foreach (var files in new[] { WpfGenerator.Generate(document, "Demo"), WinFormsGenerator.Generate(document, "Demo"), WinUIGenerator.Generate(document, "Demo"), MauiGenerator.Generate(document, "Demo") })
        {
            Assert.Contains("partial void OnSave() => Server = \"saved\";", File(files, "SettingsViewModel.cs"));
        }

        var blazor = File(BlazorGenerator.Generate(document, "Demo"), "Components/Pages/SettingsViewModel.cs");
        Assert.Contains("namespace Demo.Components.Pages;", blazor);
    }

    [Fact]
    public void AnExportNeverReplacesAFileTheBuilderDidNotWrite()
    {
        var parent = Directory.CreateTempSubdirectory("uib-code-").FullName;
        try
        {
            var document = Sample();
            var folder = WpfExporter.Export(document, parent).ProjectFolder;
            var handWritten = Path.Combine(folder, "SettingsViewModel.cs");
            File.WriteAllText(handWritten, "namespace LayoutDemo;\npublic partial class SettingsViewModel { }\n");
            var mainXaml = File.ReadAllText(Path.Combine(folder, "SettingsWindow.xaml"));

            var error = Assert.Throws<ExportException>(() => WpfExporter.Export(document with { Name = "Layout demo" }, parent));
            Assert.Contains("SettingsViewModel.cs", error.Message);
            Assert.Equal("namespace LayoutDemo;\npublic partial class SettingsViewModel { }\n", File.ReadAllText(handWritten));
            Assert.Equal(mainXaml, File.ReadAllText(Path.Combine(folder, "SettingsWindow.xaml")));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
