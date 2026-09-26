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

/// <summary>The sample's Save button is enabled while its secure-connection CheckBox is checked.</summary>
public class EnabledBindingTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    private static string File(IReadOnlyList<GeneratedFile> files, string path) =>
        files.Single(f => f.RelativePath == path).Content;

    [Fact]
    public void AButtonIsEnabledByAnOnOrOffProperty()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 10, 10);
        var check = editor.AddControl(ControlType.CheckBox, 10, 60);
        var box = editor.AddControl(ControlType.TextBox, 10, 100);
        Assert.Null(editor.SetBinding(box.Id, "Name"));
        Assert.Contains("not of the same kind", editor.SetEnabledBinding(button.Id, "Name"));
        Assert.Equal("A TextBox does not have an enabled binding.", editor.SetEnabledBinding(box.Id, "CanSave"));

        // Alone, the property starts on, so the button starts enabled.
        Assert.Null(editor.SetEnabledBinding(button.Id, "CanSave"));
        var alone = Assert.Single(DataBindings.Properties(editor.Screen), p => p.Name == "CanSave");
        Assert.Equal((BindingKind.Flag, true), (alone.Kind, alone.OnlyEnables));
        Assert.Contains("private bool _canSave = true;", WpfGenerator.ViewModelCode(editor.Document, editor.Screen, "Demo"));

        // Shared with a CheckBox, it starts as the CheckBox is designed.
        Assert.Null(editor.SetBinding(check.Id, "CanSave"));
        var shared = Assert.Single(DataBindings.Properties(editor.Screen), p => p.Name == "CanSave");
        Assert.Equal(check.Id, shared.First.Id);
        Assert.Contains("private bool _canSave = false;", WpfGenerator.ViewModelCode(editor.Document, editor.Screen, "Demo"));
        Assert.Contains("not of the same kind", editor.SetBinding(box.Id, "CanSave"));

        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains("\"enabledBinding\": \"CanSave\"", json);
        Assert.Equal("CanSave", ProjectFile.Deserialize(json).MainScreen.Controls[0].Properties.EnabledBinding);
        editor.Undo();
        editor.Undo();
        Assert.Null(editor.FindControl(button.Id)!.Properties.EnabledBinding);
    }

    [Fact]
    public void EveryTargetBindsTheButtonsEnabledState()
    {
        var document = Sample();
        var settings = document.Screens[1];
        string? Attribute(string xaml, string attribute) =>
            (string?)XDocument.Parse(xaml).Root!.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "SaveSettingsButton").Attribute(attribute);

        Assert.Equal("{Binding UseSecureConnection}", Attribute(WpfGenerator.WindowXaml(document, settings, "Demo"), "IsEnabled"));
        Assert.Equal("{x:Bind IsTrue(ViewModel.UseSecureConnection), Mode=OneWay}", Attribute(WinUIGenerator.WindowXaml(document, settings, "Demo"), "IsEnabled"));
        Assert.Contains("private static bool IsTrue(bool? value) => value == true;", WinUIGenerator.WindowGeneratedCode(document, settings, "Demo"));
        Assert.Contains("IsEnabled=\"{Binding UseSecureConnection}\"", File(MauiGenerator.Generate(document, "Demo"), "SettingsPage.xaml"));
        Assert.Contains("this.SaveSettingsButton.DataBindings.Add(\"Enabled\", this.ViewModel, \"UseSecureConnection\", true, System.Windows.Forms.DataSourceUpdateMode.Never);",
            WinFormsGenerator.DesignerCode(document, settings, "Demo"));
        Assert.Contains("disabled=\"@(!ViewModel.UseSecureConnection)\"", BlazorGenerator.PageRazor(document, settings));
        Assert.Contains("/// <summary>Bound to SecureCheckBox, SaveSettingsButton.</summary>", WpfGenerator.ViewModelCode(document, settings, "Demo"));
    }

    [Fact]
    public void TheWpfImportReadsTheEnabledBindingBack()
    {
        var document = Sample();
        var windows = document.Screens.Select(s => new WpfImporter.WindowSource(
            WpfGenerator.ClassName(document, s) + ".xaml",
            WpfGenerator.WindowXaml(document, s, "Demo"),
            WpfGenerator.EventsCode(document, s, "Demo"))).ToList();

        var imported = WpfImporter.Import(windows, "Demo", _ => null).Document;
        Assert.Equal("UseSecureConnection", ControlTree.All(imported.Screens[1].Controls).Single(c => c.Name == "SaveSettingsButton").Properties.EnabledBinding);
    }
}
