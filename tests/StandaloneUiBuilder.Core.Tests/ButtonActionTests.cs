using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>Buttons that open another screen or close their own.</summary>
public class ButtonActionTests
{
    /// <summary>Main has an Open button for Settings; Settings has a Close button.</summary>
    private static (DesignEditor Editor, ControlDocument Open, ControlDocument Close, ScreenDocument Settings) TwoScreens()
    {
        var editor = new DesignEditor();
        var open = editor.AddControl(ControlType.Button, 10, 10);
        editor.Rename(open.Id, "OpenButton");
        var settings = editor.AddScreen();
        editor.RenameScreen("Settings");
        var close = editor.AddControl(ControlType.Button, 10, 10);
        editor.Rename(close.Id, "CloseButton");
        editor.SetButtonAction(close.Id, opensScreen: null, closesScreen: true);
        editor.SelectScreen("main");
        editor.SetButtonAction(open.Id, settings.Id, closesScreen: false);
        return (editor, editor.FindControl(open.Id)!, ControlTree.Find(editor.Document.Screens[1].Controls, close.Id)!, editor.Document.Screens[1]);
    }

    [Fact]
    public void AButtonCanOpenAScreenOrCloseItsOwn()
    {
        var (editor, open, close, settings) = TwoScreens();

        Assert.Equal((settings.Id, (bool?)null), (open.Properties.OpensScreen, open.Properties.ClosesScreen));
        Assert.Equal(((string?)null, (bool?)true), (close.Properties.OpensScreen, close.Properties.ClosesScreen));
        Assert.Empty(DocumentValidator.Validate(editor.Document));

        Assert.Null(editor.SetButtonAction(open.Id, null, false));
        Assert.Null(editor.FindControl(open.Id)!.Properties.OpensScreen);
        editor.Undo();
        Assert.Equal(settings.Id, editor.FindControl(open.Id)!.Properties.OpensScreen);
    }

    [Fact]
    public void ActionsAreCheckedWhenSet()
    {
        var (editor, open, _, settings) = TwoScreens();
        var label = editor.AddControl(ControlType.Label, 10, 100);

        Assert.Equal("A button can open a screen or close its own, not both.", editor.SetButtonAction(open.Id, settings.Id, true));
        Assert.Equal("That screen no longer exists.", editor.SetButtonAction(open.Id, "gone", false));
        Assert.Equal("A Label does not have an action.", editor.SetButtonAction(label.Id, settings.Id, false));
    }

    [Fact]
    public void DeletingAScreenUnlinksButtonsThatOpenedIt()
    {
        var (editor, open, _, settings) = TwoScreens();
        editor.SelectScreen(settings.Id);

        editor.DeleteScreen();

        Assert.Null(editor.FindControl(open.Id)!.Properties.OpensScreen);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
        editor.Undo();
        Assert.Equal(settings.Id, ControlTree.Find(editor.Document.MainScreen.Controls, open.Id)!.Properties.OpensScreen);
    }

    [Fact]
    public void ActionsSurviveSaveAndAMissingScreenIsRejected()
    {
        var (editor, open, _, settings) = TwoScreens();
        var json = ProjectFile.Serialize(editor.Document);

        Assert.Contains($"\"opensScreen\": \"{settings.Id}\"", json);
        Assert.Contains("\"closesScreen\": true", json);
        Assert.Equal(settings.Id, ControlTree.Find(ProjectFile.Deserialize(json).MainScreen.Controls, open.Id)!.Properties.OpensScreen);

        var broken = json.Replace($"\"opensScreen\": \"{settings.Id}\"", "\"opensScreen\": \"gone\"", StringComparison.Ordinal);
        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(broken));
        Assert.Contains("Control \"OpenButton\" opens a screen that does not exist (\"gone\").", error.Message);
    }

    [Fact]
    public void OnlyButtonsKeepActions()
    {
        var (editor, _, _, settings) = TwoScreens();
        var json = ProjectFile.Serialize(editor.Document).Replace("\"type\": \"Button\"", "\"type\": \"Label\"", StringComparison.Ordinal);

        var reloaded = ProjectFile.Deserialize(json);

        Assert.All(reloaded.Screens.SelectMany(s => ControlTree.All(s.Controls)), c => Assert.Null(c.Properties.OpensScreen ?? (object?)c.Properties.ClosesScreen));
    }

    [Fact]
    public void WpfHandlersRunTheHookThenOpenOrClose()
    {
        var (editor, _, _, settings) = TwoScreens();
        var document = editor.Document;

        var main = WpfGenerator.EventsCode(document, "Demo");
        var second = WpfGenerator.EventsCode(document, settings, "Demo");

        Assert.Contains("""
                private void OpenButton_Click(object sender, RoutedEventArgs e)
                {
                    OnOpenButtonClick(e);
                    new SettingsWindow { Owner = this }.ShowDialog();
                }
            """.ReplaceLineEndings(), main.ReplaceLineEndings());
        Assert.Contains("        Close();", second);
    }

    [Fact]
    public void WinFormsHandlersRunTheHookThenOpenOrClose()
    {
        var (editor, _, _, settings) = TwoScreens();
        var document = editor.Document;

        var main = WinFormsGenerator.EventsCode(document, "Demo");
        var second = WinFormsGenerator.EventsCode(document, settings, "Demo");

        Assert.Contains("        OnOpenButtonClick(e);", main);
        Assert.Contains("        using var form = new SettingsForm();", main);
        Assert.Contains("        form.ShowDialog(this);", main);
        Assert.Contains("        Close();", second);
        foreach (var code in new[] { main, second, WpfGenerator.EventsCode(document, "Demo") })
        {
            var errors = CSharpSyntaxTree.ParseText(code).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }
    }

    [Fact]
    public void TheOpenedWindowFollowsTheScreensCurrentName()
    {
        var (editor, _, _, settings) = TwoScreens();
        editor.SelectScreen(settings.Id);
        editor.RenameScreen("Options");

        Assert.Contains("new OptionsWindow { Owner = this }.ShowDialog();", WpfGenerator.EventsCode(editor.Document, "Demo"));
    }
}
