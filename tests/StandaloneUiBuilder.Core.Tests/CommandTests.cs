using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Import.Wpf;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>The sample's Settings screen has a Save button that runs the Save command.</summary>
public class CommandTests
{
    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    private static string File(IReadOnlyList<GeneratedFile> files, string path) =>
        files.Single(f => f.RelativePath == path).Content;

    [Fact]
    public void OnlyButtonsHaveCommandsAndTheyAreStored()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);
        var box = editor.AddControl(ControlType.TextBox, 10, 60);
        Assert.Null(editor.SetCommand(button.Id, "Save"));
        Assert.Equal("A TextBox does not have a command.", editor.SetCommand(box.Id, "Save"));

        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains("\"command\": \"Save\"", json);
        Assert.Equal("Save", ProjectFile.Deserialize(json).MainScreen.Controls[0].Properties.Command);
        Assert.True(DataBindings.HasViewModel(editor.Screen));

        editor.Undo();
        Assert.Null(editor.FindControl(button.Id)!.Properties.Command);
    }

    [Fact]
    public void CommandsAndBindingsDoNotClashInTheViewModel()
    {
        var editor = new DesignEditor();
        var save = editor.AddControl(ControlType.Button, 10, 10);
        var again = editor.AddControl(ControlType.Button, 120, 10);
        var box = editor.AddControl(ControlType.TextBox, 10, 60);
        var total = editor.AddControl(ControlType.TextBox, 10, 100);
        Assert.Null(editor.SetBinding(box.Id, "Name"));
        Assert.Null(editor.SetCommand(save.Id, "Save"));

        // Two buttons may run the same command.
        Assert.Null(editor.SetCommand(again.Id, "Save"));
        Assert.Equal([save.Id, again.Id], DataBindings.Commands(editor.Screen).Single().Buttons.Select(b => b.Id));

        // A command and a property cannot share a name, or a hook's.
        Assert.Contains("would clash with the binding \"Name\"", editor.SetCommand(again.Id, "Name"));
        Assert.Contains("would clash with the command \"Save\"", editor.SetBinding(total.Id, "Save"));
        Assert.Contains("would clash with the binding \"Name\"", editor.SetCommand(again.Id, "NameChanged"));
        Assert.Contains("only in case", editor.SetCommand(again.Id, "SAVE"));
        Assert.Contains("capital letter", editor.SetCommand(again.Id, "save"));
    }

    [Fact]
    public void TheViewModelHasAMethodPerCommand()
    {
        var document = Sample();
        var code = WpfGenerator.ViewModelCode(document, document.Screens[1], "Demo");
        Assert.Contains("/// <summary>Run by SaveSettingsButton when clicked; implement OnSave in your part of the class.</summary>", code);
        Assert.Contains("public void Save() => OnSave();", code);
        Assert.Contains("partial void OnSave();", code);
    }

    [Fact]
    public void EveryTargetsClickHandlerRunsTheCommandAfterTheHook()
    {
        var document = Sample();
        var settings = document.Screens[1];
        static void InOrder(string code, params string[] parts)
        {
            var at = 0;
            foreach (var part in parts)
            {
                var found = code.IndexOf(part, at, StringComparison.Ordinal);
                Assert.True(found >= 0, $"\"{part}\" is missing or out of order in:\n{code}");
                at = found + part.Length;
            }
        }

        InOrder(WpfGenerator.EventsCode(document, settings, "Demo"), "SaveSettingsButton_Click(", "OnSaveSettingsButtonClick(e);", "ViewModel.Save();");
        InOrder(WinFormsGenerator.EventsCode(document, settings, "Demo"), "SaveSettingsButton_Click(", "OnSaveSettingsButtonClick(e);", "ViewModel.Save();");
        InOrder(WinUIGenerator.WindowGeneratedCode(document, settings, "Demo"), "SaveSettingsButton_Click(", "OnSaveSettingsButtonClick(e);", "ViewModel.Save();");
        InOrder(File(MauiGenerator.Generate(document, "Demo"), "SettingsPage.g.cs"), "SaveSettingsButton_Clicked(", "OnSaveSettingsButtonClicked(e);", "ViewModel.Save();");
        InOrder(File(BlazorGenerator.Generate(document, "Demo"), "Components/Pages/SettingsPage.Events.g.cs"), "SaveSettingsButton_Click(", "OnSaveSettingsButtonClick();", "ViewModel.Save();");

        // With an action as well, the command runs first.
        var both = document.WithScreen(settings with
        {
            Controls = ControlTree.Replace(settings.Controls, ControlTree.All(settings.Controls).Single(c => c.Name == "CloseSettingsButton").Id,
                c => c with { Properties = c.Properties with { Command = "Save" } }),
        });
        InOrder(WpfGenerator.EventsCode(both, both.Screens[1], "Demo"), "CloseSettingsButton_Click(", "OnCloseSettingsButtonClick(e);", "ViewModel.Save();", "Close();");
        InOrder(WinFormsGenerator.EventsCode(both, both.Screens[1], "Demo"), "CloseSettingsButton_Click(", "ViewModel.Save();", "Close();");
        InOrder(WinUIGenerator.WindowGeneratedCode(both, both.Screens[1], "Demo"), "CloseSettingsButton_Click(", "ViewModel.Save();", "Close();");
        InOrder(File(MauiGenerator.Generate(both, "Demo"), "SettingsPage.g.cs"), "CloseSettingsButton_Clicked(", "ViewModel.Save();", "await CloseAsync();");
        InOrder(File(BlazorGenerator.Generate(both, "Demo"), "Components/Pages/SettingsPage.Events.g.cs"), "CloseSettingsButton_Click(", "ViewModel.Save();", "history.back");
    }

    [Fact]
    public void AScreenWithOnlyACommandHasAViewModel()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);
        editor.SetCommand(button.Id, "Refresh");
        var files = WpfGenerator.Generate(editor.Document, "Demo");
        Assert.Contains("public void Refresh() => OnRefresh();", File(files, "MainViewModel.g.cs"));
        Assert.Contains("DataContext=", File(files, "MainWindow.xaml"));
    }

    [Fact]
    public void TheWpfImportReadsCommandsBack()
    {
        var document = Sample();
        var windows = document.Screens.Select(s => new WpfImporter.WindowSource(
            WpfGenerator.ClassName(document, s) + ".xaml",
            WpfGenerator.WindowXaml(document, s, "Demo"),
            WpfGenerator.EventsCode(document, s, "Demo"))).ToList();

        var imported = WpfImporter.Import(windows, "Demo", _ => null).Document;
        var save = ControlTree.All(imported.Screens[1].Controls).Single(c => c.Name == "SaveSettingsButton");
        Assert.Equal("Save", save.Properties.Command);
        Assert.True(ControlTree.All(imported.Screens[1].Controls).Single(c => c.Name == "CloseSettingsButton").Properties.ClosesScreen);
    }

    [Fact]
    public void APastedCommandIsDroppedWhereItWouldClash()
    {
        var editor = new DesignEditor();
        var box = editor.AddControl(ControlType.TextBox, 10, 10);
        var button = editor.AddControl(ControlType.Button, 10, 60);
        editor.SetBinding(box.Id, "Save");
        var copy = editor.FindControl(button.Id)! with { Properties = editor.FindControl(button.Id)!.Properties with { Command = "Save" } };
        Assert.Null(Assert.Single(editor.PasteControls([copy], 10)).Properties.Command);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }
}
