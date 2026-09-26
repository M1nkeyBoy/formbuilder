namespace StandaloneUiBuilder.Core;

/// <summary>Commands for the order Tab moves through a screen's controls.</summary>
public sealed partial class DesignEditor
{
    /// <summary>
    /// Sets the current screen's tab order. Controls it leaves out follow in their usual order;
    /// IDs of controls that do not take input are ignored. One undo step if it changes.
    /// </summary>
    public bool SetTabOrder(IReadOnlyList<Guid> ids)
    {
        var screen = Screen;
        var ordered = TabSequence.Tidy(screen with { TabOrder = [.. ids] });
        if (TabSequence.Resolve(ordered).Select(c => c.Id).SequenceEqual(TabSequence.Resolve(screen).Select(c => c.Id))
            && ordered.TabOrder is null == screen.TabOrder is null)
        {
            return false;
        }

        Commit(Document.WithScreen(ordered));
        return true;
    }

    /// <summary>Moves a control to a place in the tab order, from 0, as when clicking controls in turn.</summary>
    public bool PutInTabOrder(Guid id, int position)
    {
        var order = TabSequence.Resolve(Screen).Select(c => c.Id).ToList();
        if (!order.Remove(id))
        {
            return false;
        }

        order.Insert(Math.Clamp(position, 0, order.Count), id);
        return SetTabOrder(order);
    }

    /// <summary>Orders Tab by where controls are: top to bottom, then left to right.</summary>
    public bool SetTabOrderByPosition() => SetTabOrder(TabSequence.ByPosition(Screen));

    /// <summary>Goes back to the order the controls are in.</summary>
    public bool ResetTabOrder()
    {
        var screen = Screen;
        if (screen.TabOrder is null)
        {
            return false;
        }

        Commit(Document.WithScreen(screen with { TabOrder = null }));
        return true;
    }
}
