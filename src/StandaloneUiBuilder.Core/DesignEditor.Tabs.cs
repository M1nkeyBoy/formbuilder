namespace StandaloneUiBuilder.Core;

/// <summary>Commands for TabControls and their pages.</summary>
public sealed partial class DesignEditor
{
    /// <summary>
    /// Adds an empty page at the end of a TabControl and shows it. The TabControl can be given
    /// by its own ID or by one of its pages. Returns the new page, or null if there is no
    /// such TabControl.
    /// </summary>
    public ControlDocument? AddTab(Guid id)
    {
        if (TabControlOf(id) is not { } tabs)
        {
            return null;
        }

        var screen = Screen;
        var page = NewPage(screen, tabs.Children!.Count + 1);
        var children = tabs.Children.Add(page);
        Replace(tabs, tabs with
        {
            Children = children,
            Properties = tabs.Properties with { SelectedTab = children.Count - 1 },
        });
        return page;
    }

    /// <summary>Shows a TabControl's page, by index, in the designer and when the screen opens.</summary>
    public string? SetSelectedTab(Guid id, int index)
    {
        if (FindControl(id) is not { Type: ControlType.TabControl } tabs)
        {
            return "The control is not a TabControl.";
        }

        if (index < 0 || index >= Math.Max(1, tabs.Children?.Count ?? 0))
        {
            return $"\"{tabs.Name}\" has no tab {index + 1}.";
        }

        if (tabs.Properties.SelectedTab != index)
        {
            Replace(tabs, tabs with { Properties = tabs.Properties with { SelectedTab = index } });
        }

        return null;
    }

    /// <summary>
    /// Makes sure a control can be seen: every TabControl it is in shows the page holding it
    /// (or, for a page, the page itself). One undo step if anything changed.
    /// </summary>
    public bool ShowControl(Guid id)
    {
        var screen = Screen;
        var controls = screen.Controls;
        for (var childId = id; ParentOf(childId) is { } parent; childId = parent.Id)
        {
            if (parent.Type == ControlType.TabControl)
            {
                var index = parent.Children!.FindIndex(c => c.Id == childId);
                if (ContainerLayout.ShownTab(parent) != index)
                {
                    controls = ControlTree.Replace(controls, parent.Id, tabs => tabs with { Properties = tabs.Properties with { SelectedTab = index } });
                }
            }
        }

        if (ReferenceEquals(controls, screen.Controls))
        {
            return false;
        }

        Commit(Document.WithScreen(screen with { Controls = controls }));
        return true;
    }

    /// <summary>The TabControl with this ID, or holding the page with this ID.</summary>
    public ControlDocument? TabControlOf(Guid id) => FindControl(id) switch
    {
        { Type: ControlType.TabControl } tabs => tabs,
        { Type: ControlType.TabPage } => ParentOf(id) is { Type: ControlType.TabControl } tabs ? tabs : null,
        _ => null,
    };
}
