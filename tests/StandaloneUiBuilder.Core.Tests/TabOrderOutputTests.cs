using System.Xml.Linq;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Import.Wpf;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>The sample's Settings screen has a tab order: its Mode group comes after the TabControl.</summary>
public class TabOrderOutputTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));

    private static string? Attribute(XElement root, XNamespace x, string name, string attribute) =>
        (string?)root.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name).Attribute(attribute);

    [Fact]
    public void WpfAndWinUINumberControlsAcrossTheScreen()
    {
        var document = Sample();
        var settings = document.Screens[1];
        var wpf = XDocument.Parse(WpfGenerator.WindowXaml(document, settings, "Demo")).Root!;
        Assert.Equal("3", Attribute(wpf, X, "StartDatePicker", "TabIndex"));
        Assert.Equal("Local", Attribute(wpf, X, "StartDatePicker", "KeyboardNavigation.TabNavigation"));
        Assert.Equal("12", Attribute(wpf, X, "LevelSlider", "TabIndex"));
        Assert.Null(Attribute(wpf, X, "SettingsLabel", "TabIndex"));

        var winui = XDocument.Parse(WinUIGenerator.WindowXaml(document, settings, "Demo")).Root!;
        Assert.Equal("3", Attribute(winui, X, "StartDatePicker", "TabIndex"));
        Assert.Equal("12", Attribute(winui, X, "LevelSlider", "TabIndex"));
        Assert.Null(Attribute(winui, X, "ModeGroup", "TabIndex"));
        Assert.Null(Attribute(winui, X, "DetailsTabs", "TabIndex"));
        Assert.Equal(2, winui.Descendants(XName.Get("ToggleButton", winui.Name.NamespaceName)).Count(e => (string?)e.Attribute("TabIndex") == "6"));

        // .NET 10's MAUI has no TabIndex, so its pages keep the default order.
        var maui = XDocument.Parse(MauiGenerator.PageXaml(document, settings, "Demo")).Root!;
        Assert.DoesNotContain(maui.Descendants(), e => e.Attribute("TabIndex") is not null);

        // Without a tab order, nothing is numbered.
        var main = XDocument.Parse(WpfGenerator.WindowXaml(document, document.MainScreen, "Demo")).Root!;
        Assert.DoesNotContain(main.Descendants(), e => e.Attribute("TabIndex") is not null);
    }

    [Fact]
    public void WinFormsRanksControlsWithinEachContainer()
    {
        var document = Sample();
        var settings = document.Screens[1];
        var code = WinFormsGenerator.DesignerCode(document, settings, "Demo");
        Assert.Contains("this.SettingsGrid.TabIndex = 0;", code);
        Assert.Contains("this.StartDatePicker.TabIndex = 1;", code);
        Assert.Contains("this.DetailsTabsHost.TabIndex = 4;", code);
        Assert.Contains("this.ModeGroup.TabIndex = 5;", code);
        Assert.Contains("this.LevelSlider.TabIndex = 2;", code);
    }

    [Fact]
    public void BlazorPutsTabindexOnWhatTakesFocus()
    {
        var document = Sample();
        var markup = BlazorGenerator.PageRazor(document, document.Screens[1]);

        Assert.Contains("id=\"LevelSlider\" style=\"height:30px;width:100%;margin-top:6px\" tabindex=\"13\"", markup);
        Assert.Contains("<input type=\"checkbox\" tabindex=\"3\"", markup);
        Assert.DoesNotContain("tabindex", BlazorGenerator.PageRazor(document, document.MainScreen));
    }

    [Fact]
    public void TheWpfImportReadsTheTabOrderBack()
    {
        var document = Sample();
        var settings = document.Screens[1];
        var windows = document.Screens.Select(s => new WpfImporter.WindowSource(
            WpfGenerator.ClassName(document, s) + ".xaml",
            WpfGenerator.WindowXaml(document, s, "Demo"),
            WpfGenerator.EventsCode(document, s, "Demo"))).ToList();

        var imported = WpfImporter.Import(windows, "Demo", _ => null).Document;

        Assert.Equal(
            TabSequence.Resolve(settings).Select(c => c.Name),
            TabSequence.Resolve(imported.Screens[1]).Select(c => c.Name));
        Assert.Null(imported.MainScreen.TabOrder);
    }
}
