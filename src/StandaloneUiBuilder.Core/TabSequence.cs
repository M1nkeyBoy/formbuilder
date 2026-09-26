namespace StandaloneUiBuilder.Core;

/// <summary>
/// The order Tab moves through a screen's controls. Only controls that take input are in it
/// (<see cref="ControlDefinition.IsTabStop"/>). A screen's <see cref="ScreenDocument.TabOrder"/>
/// lists them in order; without one, and for controls it does not list, the order is the order
/// the controls are in (containers' children in place).
/// </summary>
public static class TabSequence
{
    /// <summary>The controls Tab visits, in order, including those on hidden tab pages.</summary>
    public static IReadOnlyList<ControlDocument> Resolve(ScreenDocument screen)
    {
        var stops = ControlTree.All(screen.Controls).Where(IsTabStop).ToList();
        if (screen.TabOrder is not { } order)
        {
            return stops;
        }

        var byId = stops.ToDictionary(c => c.Id);
        var listed = order.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var ids = listed.Select(c => c.Id).ToHashSet();
        return [.. listed, .. stops.Where(c => !ids.Contains(c.Id))];
    }

    /// <summary>Each control's place in <see cref="Resolve"/>, from 0.</summary>
    public static IReadOnlyDictionary<Guid, int> Indexes(ScreenDocument screen) =>
        Resolve(screen).Select((control, index) => (control.Id, index)).ToDictionary(p => p.Id, p => p.index);

    /// <summary>
    /// For targets whose tab order is kept within each container: every control's rank among
    /// the controls beside it (on the screen or in the same container), from 0. A container
    /// ranks by the first of its controls that Tab visits, so its controls are visited
    /// together; controls Tab never visits keep their place after the others.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> SiblingRanks(ScreenDocument screen)
    {
        var indexes = Indexes(screen);
        var ranks = new Dictionary<Guid, int>();

        int First(ControlDocument control) =>
            ControlTree.All([control]).Select(c => indexes.TryGetValue(c.Id, out var i) ? i : int.MaxValue).DefaultIfEmpty(int.MaxValue).Min();

        void Rank(IReadOnlyList<ControlDocument> siblings)
        {
            var ordered = siblings.Select((control, position) => (control, position))
                .OrderBy(s => First(s.control)).ThenBy(s => s.position).ToList();
            for (var rank = 0; rank < ordered.Count; rank++)
            {
                ranks[ordered[rank].control.Id] = rank;
                if (ordered[rank].control.Children is { } children)
                {
                    Rank(children);
                }
            }
        }

        Rank(screen.Controls);
        return ranks;
    }

    /// <summary>
    /// The controls that take input sorted by where they are on the screen: top to bottom, and
    /// left to right among controls whose tops are within half a grid step of each other.
    /// </summary>
    public static IReadOnlyList<Guid> ByPosition(ScreenDocument screen)
    {
        var placed = ContainerLayout.Flatten(screen).Where(p => IsTabStop(p.Control)).ToList();
        var tolerance = Math.Max(1, screen.GridSize / 2);
        var rows = new List<List<PlacedControl>>();
        foreach (var item in placed.OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X))
        {
            if (rows.Count > 0 && item.Bounds.Y - rows[^1][0].Bounds.Y <= tolerance)
            {
                rows[^1].Add(item);
            }
            else
            {
                rows.Add([item]);
            }
        }

        return [.. rows.SelectMany(row => row.OrderBy(p => p.Bounds.X)).Select(p => p.Control.Id)];
    }

    /// <summary>
    /// A screen whose tab order lists only controls still on it that take input, once each,
    /// or has none if that leaves it the same as the default.
    /// </summary>
    public static ScreenDocument Tidy(ScreenDocument screen)
    {
        if (screen.TabOrder is not { } order)
        {
            return screen;
        }

        var stops = ControlTree.All(screen.Controls).Where(IsTabStop).Select(c => c.Id).ToList();
        var valid = stops.ToHashSet();
        var kept = order.Distinct().Where(valid.Contains).ToList();
        var resolved = Resolve(screen with { TabOrder = [.. kept] }).Select(c => c.Id);
        if (resolved.SequenceEqual(stops))
        {
            return screen with { TabOrder = null };
        }

        return kept.SequenceEqual(order) ? screen : screen with { TabOrder = [.. kept] };
    }

    public static bool IsTabStop(ControlDocument control) =>
        ControlCatalog.TryGet(control.Type, out var definition) && definition.IsTabStop;
}
