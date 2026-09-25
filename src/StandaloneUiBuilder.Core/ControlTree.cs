using System.Collections.Immutable;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Helpers for the tree of controls: containers hold children, which may be containers too.
/// All edits return new immutable lists.
/// </summary>
public static class ControlTree
{
    /// <summary>Every control, parents before their children, in draw order.</summary>
    public static IEnumerable<ControlDocument> All(IEnumerable<ControlDocument> roots)
    {
        foreach (var control in roots)
        {
            yield return control;
            if (control.Children is { } children)
            {
                foreach (var descendant in All(children))
                {
                    yield return descendant;
                }
            }
        }
    }

    public static ControlDocument? Find(IEnumerable<ControlDocument> roots, Guid id) =>
        All(roots).FirstOrDefault(c => c.Id == id);

    /// <summary>The container holding a control, or null if it is on the screen itself.</summary>
    public static ControlDocument? ParentOf(IEnumerable<ControlDocument> roots, Guid id) =>
        All(roots).FirstOrDefault(c => c.Children?.Any(child => child.Id == id) == true);

    public static bool IsRoot(ImmutableList<ControlDocument> roots, Guid id) => roots.Any(c => c.Id == id);

    /// <summary>True if <paramref name="id"/> is <paramref name="ancestorId"/> or inside it.</summary>
    public static bool IsSelfOrDescendant(IEnumerable<ControlDocument> roots, Guid ancestorId, Guid id) =>
        Find(roots, ancestorId) is { } ancestor && All([ancestor]).Any(c => c.Id == id);

    public static ImmutableList<ControlDocument> Replace(ImmutableList<ControlDocument> roots, Guid id, Func<ControlDocument, ControlDocument> change) =>
        roots.ConvertAll(control =>
            control.Id == id ? change(control)
            : control.Children is { } children ? control with { Children = Replace(children, id, change) }
            : control);

    public static ImmutableList<ControlDocument> Remove(ImmutableList<ControlDocument> roots, IReadOnlyCollection<Guid> ids) =>
        roots.RemoveAll(c => ids.Contains(c.Id)).ConvertAll(control =>
            control.Children is { } children ? control with { Children = Remove(children, ids) } : control);

    /// <summary>Inserts a control into a container (or the screen when null) at an index.</summary>
    public static ImmutableList<ControlDocument> Insert(ImmutableList<ControlDocument> roots, Guid? parentId, int index, ControlDocument control)
    {
        if (parentId is null)
        {
            return roots.Insert(Math.Clamp(index, 0, roots.Count), control);
        }

        return Replace(roots, parentId.Value, parent =>
        {
            var children = parent.Children ?? [];
            return parent with { Children = children.Insert(Math.Clamp(index, 0, children.Count), control) };
        });
    }

    /// <summary>Gives a control and everything inside it new IDs.</summary>
    public static ControlDocument WithNewIds(ControlDocument control) => control with
    {
        Id = Guid.NewGuid(),
        Children = control.Children?.ConvertAll(WithNewIds),
    };
}
