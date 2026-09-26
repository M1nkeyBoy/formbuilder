using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class TabTests
{
    private static (DesignEditor Editor, ControlDocument Tabs) NewTabs()
    {
        var editor = new DesignEditor();
        var tabs = editor.AddControl(ControlType.TabControl, 20, 20);
        return (editor, editor.FindControl(tabs.Id)!);
    }

    private static PlacedControl Placed(DesignEditor editor, Guid id) =>
        ContainerLayout.Flatten(editor.Screen).Single(p => p.Control.Id == id);

    [Fact]
    public void ANewTabControlHasTwoPagesAndShowsTheFirst()
    {
        var (_, tabs) = NewTabs();

        Assert.Equal(["TabPage1", "TabPage2"], tabs.Children!.Select(p => p.Name));
        Assert.Equal(["Tab 1", "Tab 2"], tabs.Children!.Select(p => p.Properties.Text));
        Assert.Equal(0, tabs.Properties.SelectedTab);
        Assert.All(tabs.Children!, p => Assert.Equal(ControlType.TabPage, p.Type));
    }

    [Fact]
    public void TabPagesAreNotInTheToolboxAndOnlyTabControlsHoldThem()
    {
        Assert.False(ControlCatalog.Get(ControlType.TabPage).InToolbox);
        Assert.True(ControlCatalog.Get(ControlType.TabControl).CanHold(ControlType.TabPage));
        Assert.False(ControlCatalog.Get(ControlType.TabControl).CanHold(ControlType.Button));
        Assert.False(ControlCatalog.Get(ControlType.StackPanel).CanHold(ControlType.TabPage));
        Assert.True(ControlCatalog.Get(ControlType.TabPage).CanHold(ControlType.TabControl));
        Assert.Throws<ArgumentException>(() => new DesignEditor().AddControl(ControlType.TabPage, 0, 0));
    }

    [Fact]
    public void PagesFillTheInsetAreaAndOnlyTheShownOneIsVisible()
    {
        var (editor, tabs) = NewTabs();
        var (left, top, right, bottom) = ContainerLayout.TabControlInset;
        var expected = new ControlBounds(tabs.X + left, tabs.Y + top, tabs.Width - left - right, tabs.Height - top - bottom);

        var first = Placed(editor, tabs.Children![0].Id);
        var second = Placed(editor, tabs.Children![1].Id);

        Assert.Equal(expected, first.Bounds);
        Assert.Equal(expected, second.Bounds);
        Assert.False(first.IsHidden);
        Assert.True(second.IsHidden);
    }

    [Fact]
    public void ControlsDropOnTheShownPageEvenOverTheTabs()
    {
        var (editor, tabs) = NewTabs();
        var shown = tabs.Children![0];

        // Over the row of tabs, above the pages.
        var target = ContainerLayout.ContainerAt(editor.Screen, tabs.X + 50, tabs.Y + 10);
        Assert.Equal(shown.Id, target!.Control.Id);

        var button = editor.AddControlTo(ControlType.Button, target.Control.Id, tabs.X + 50, tabs.Y + 10);
        Assert.Equal(shown.Id, editor.ParentOf(button!.Id)!.Id);
        Assert.Null(editor.AddControlTo(ControlType.Button, tabs.Id, tabs.X + 50, tabs.Y + 50));

        // A page is only ever dropped on a TabControl.
        var forPage = ContainerLayout.ContainerAt(editor.Screen, tabs.X + 50, tabs.Y + 50, type: ControlType.TabPage);
        Assert.Equal(tabs.Id, forPage!.Control.Id);
    }

    [Fact]
    public void ControlsOnHiddenPagesAreHiddenAndCannotBeDroppedOn()
    {
        var (editor, tabs) = NewTabs();
        var second = tabs.Children![1];
        var inner = editor.AddControlTo(ControlType.StackPanel, tabs.Children![0].Id, tabs.X + 50, tabs.Y + 50)!;

        Assert.Null(editor.SetSelectedTab(tabs.Id, 1));

        Assert.True(Placed(editor, inner.Id).IsHidden);
        Assert.Equal(second.Id, ContainerLayout.ContainerAt(editor.Screen, tabs.X + 50, tabs.Y + 50)!.Control.Id);
        Assert.NotNull(editor.SetSelectedTab(tabs.Id, 2));

        editor.Undo();
        Assert.Equal(0, editor.FindControl(tabs.Id)!.Properties.SelectedTab);
    }

    [Fact]
    public void AddTabAppendsAPageAndShowsIt()
    {
        var (editor, tabs) = NewTabs();

        var page = editor.AddTab(tabs.Children![0].Id);

        var updated = editor.FindControl(tabs.Id)!;
        Assert.Equal("TabPage3", page!.Name);
        Assert.Equal("Tab 3", page.Properties.Text);
        Assert.Equal(2, updated.Properties.SelectedTab);
        Assert.Null(editor.AddTab(Guid.NewGuid()));
    }

    [Fact]
    public void DeletingTheShownPageShowsAnotherOne()
    {
        var (editor, tabs) = NewTabs();
        editor.SetSelectedTab(tabs.Id, 1);

        editor.DeleteControl(tabs.Children![1].Id);

        Assert.Equal(0, editor.FindControl(tabs.Id)!.Properties.SelectedTab);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void ReorderingPagesKeepsTheShownPage()
    {
        var (editor, tabs) = NewTabs();
        var second = tabs.Children![1];
        editor.SetSelectedTab(tabs.Id, 1);

        Assert.True(editor.MoveWithinContainer(second.Id, -1));

        var updated = editor.FindControl(tabs.Id)!;
        Assert.Equal(second.Id, updated.Children![0].Id);
        Assert.Equal(0, updated.Properties.SelectedTab);
    }

    [Fact]
    public void PagesStayInTabControlsAndTabControlsHoldOnlyPages()
    {
        var (editor, tabs) = NewTabs();
        var other = editor.AddControl(ControlType.TabControl, 400, 300);
        var stack = editor.AddControl(ControlType.StackPanel, 400, 20);
        var button = editor.AddControl(ControlType.Button, 10, 400);
        var page = tabs.Children![1];

        Assert.NotNull(editor.MoveToScreen(page.Id, 10, 10));
        Assert.NotNull(editor.MoveIntoContainer(page.Id, stack.Id, 410, 30));
        Assert.NotNull(editor.MoveIntoContainer(button.Id, other.Id, 410, 310));

        Assert.Null(editor.MoveIntoContainer(page.Id, other.Id, 410, 310));
        var moved = editor.FindControl(other.Id)!;
        Assert.Equal(page.Id, moved.Children![2].Id);
        Assert.Equal(2, moved.Properties.SelectedTab);
        Assert.Single(editor.FindControl(tabs.Id)!.Children!);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void ShowControlSwitchesEveryTabControlOnTheWay()
    {
        var (editor, tabs) = NewTabs();
        editor.SetBounds(tabs.Id, new ControlBounds(20, 20, 500, 400));
        editor.SetSelectedTab(tabs.Id, 1);
        var inner = editor.AddControlTo(ControlType.TabControl, tabs.Children![1].Id, 100, 100)!;
        var innerPage = editor.FindControl(inner.Id)!.Children![1];
        var label = editor.AddTab(inner.Id);
        editor.SetSelectedTab(tabs.Id, 0);
        editor.SetSelectedTab(inner.Id, 0);

        Assert.True(editor.ShowControl(innerPage.Id));

        Assert.Equal(1, editor.FindControl(tabs.Id)!.Properties.SelectedTab);
        Assert.Equal(1, editor.FindControl(inner.Id)!.Properties.SelectedTab);
        Assert.False(Placed(editor, innerPage.Id).IsHidden);
        Assert.False(editor.ShowControl(innerPage.Id));
        Assert.NotNull(label);
    }

    [Fact]
    public void PastingAPageOnItsOwnIsSkipped()
    {
        var (editor, tabs) = NewTabs();

        Assert.Empty(editor.PasteControls([tabs.Children![0]], 10));

        var copies = editor.PasteControls([editor.FindControl(tabs.Id)!], 10);
        Assert.Equal(["TabPage3", "TabPage4"], copies.Single().Children!.Select(p => p.Name));
    }

    [Fact]
    public void TheValidatorRejectsMisplacedPagesAndTabsOutOfRange()
    {
        var (editor, tabs) = NewTabs();
        var document = editor.Document;
        var screen = document.MainScreen;
        var page = tabs.Children![0];

        var loosePage = document.WithScreen(screen with { Controls = screen.Controls.Add(page with { Id = Guid.NewGuid(), Name = "Loose", X = 0, Y = 0 }) });
        Assert.Contains(DocumentValidator.Validate(loosePage), e => e.Contains("must be inside a TabControl"));

        var withButton = document.WithScreen(screen with
        {
            Controls = ControlTree.Insert(screen.Controls, tabs.Id, 0, new ControlDocument
            {
                Id = Guid.NewGuid(), Type = ControlType.Button, Name = "Stray", Width = 100, Height = 30,
                Properties = new ControlProperties { Text = "Stray" },
            }),
        });
        Assert.Contains(DocumentValidator.Validate(withButton), e => e.Contains("can hold only tab pages"));

        var outOfRange = document.WithScreen(screen with
        {
            Controls = ControlTree.Replace(screen.Controls, tabs.Id, t => t with { Properties = t.Properties with { SelectedTab = 5 } }),
        });
        Assert.Contains(DocumentValidator.Validate(outOfRange), e => e.Contains("tab shown (6)"));
    }

    [Fact]
    public void TabsSurviveSavingAndLoading()
    {
        var (editor, tabs) = NewTabs();
        editor.SetSelectedTab(tabs.Id, 1);
        editor.SetText(tabs.Children![1].Id, "Advanced");

        var reloaded = ProjectFile.Deserialize(ProjectFile.Serialize(editor.Document));

        var loaded = ControlTree.Find(reloaded.MainScreen.Controls, tabs.Id)!;
        Assert.Equal(1, loaded.Properties.SelectedTab);
        Assert.Equal("Advanced", loaded.Children![1].Properties.Text);
        Assert.Equal(StackOrientation.Vertical, loaded.Children![1].Properties.Orientation);
        Assert.Null(loaded.Children![1].Properties.SelectedTab);
    }
}
