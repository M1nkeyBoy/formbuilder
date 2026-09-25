namespace StandaloneUiBuilder.Core;

/// <summary>How to line controls up with a reference control.</summary>
public enum AlignTo
{
    Lefts,
    Centers,
    Rights,
    Tops,
    Middles,
    Bottoms,
}

/// <summary>Which dimensions to copy from a reference control.</summary>
public enum SameSize
{
    Width,
    Height,
    Both,
}

public sealed partial class DesignEditor
{
    /// <summary>
    /// Lines controls on the screen up with a reference control (the one selected last), as one
    /// step. Controls inside containers are placed by their container and are left alone.
    /// Returns false if nothing moved.
    /// </summary>
    public bool Align(IReadOnlyList<Guid> ids, Guid reference, AlignTo edge)
    {
        if (RootControls(ids, reference) is not var (screen, target, others) || others.Count == 0)
        {
            return false;
        }

        return Arrange(screen, others, control => edge switch
        {
            AlignTo.Lefts => control with { X = target.X },
            AlignTo.Centers => control with { X = target.X + (target.Width - control.Width) / 2 },
            AlignTo.Rights => control with { X = target.Right() - control.Width },
            AlignTo.Tops => control with { Y = target.Y },
            AlignTo.Middles => control with { Y = target.Y + (target.Height - control.Height) / 2 },
            _ => control with { Y = target.Bottom() - control.Height },
        });
    }

    /// <summary>
    /// Gives controls on the screen the width, height or both of a reference control, as one
    /// step, keeping each at least its type's minimum size. Returns false if nothing changed.
    /// </summary>
    public bool MakeSameSize(IReadOnlyList<Guid> ids, Guid reference, SameSize size)
    {
        if (RootControls(ids, reference) is not var (screen, target, others) || others.Count == 0)
        {
            return false;
        }

        return Arrange(screen, others, control =>
        {
            var definition = ControlCatalog.Get(control.Type);
            return control with
            {
                Width = size == SameSize.Height ? control.Width : Math.Max(target.Width, definition.MinWidth),
                Height = size == SameSize.Width ? control.Height : Math.Max(target.Height, definition.MinHeight),
            };
        });
    }

    /// <summary>
    /// Spaces three or more controls on the screen evenly between the outermost two, which
    /// stay where they are, as one step. Returns false if nothing moved.
    /// </summary>
    public bool Distribute(IReadOnlyList<Guid> ids, bool horizontally)
    {
        var screen = Screen;
        var controls = screen.Controls.Where(c => ids.Contains(c.Id)).ToList();
        if (controls.Count < 3)
        {
            return false;
        }

        var ordered = horizontally ? controls.OrderBy(c => c.X).ThenBy(c => c.Y).ToList() : controls.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        var start = horizontally ? ordered[0].X : ordered[0].Y;
        var end = horizontally ? ordered[^1].Right() : ordered[^1].Bottom();
        var total = ordered.Sum(c => horizontally ? c.Width : c.Height);
        var gap = (end - start - total) / (double)(ordered.Count - 1);

        var positions = new Dictionary<Guid, int>();
        var offset = (double)start;
        foreach (var control in ordered)
        {
            positions[control.Id] = (int)Math.Round(offset);
            offset += (horizontally ? control.Width : control.Height) + gap;
        }

        return Arrange(screen, ordered, control => horizontally ? control with { X = positions[control.Id] } : control with { Y = positions[control.Id] });
    }

    /// <summary>The screen, the reference control and the other controls, all directly on the screen.</summary>
    private (ScreenDocument Screen, ControlDocument Reference, List<ControlDocument> Others)? RootControls(IReadOnlyList<Guid> ids, Guid reference)
    {
        var screen = Screen;
        if (screen.Controls.FirstOrDefault(c => c.Id == reference) is not { } target)
        {
            return null;
        }

        return (screen, target, screen.Controls.Where(c => c.Id != reference && ids.Contains(c.Id)).ToList());
    }

    /// <summary>Applies a change to some controls, keeping each inside the screen, as one step.</summary>
    private bool Arrange(ScreenDocument screen, IReadOnlyCollection<ControlDocument> controls, Func<ControlDocument, ControlDocument> change)
    {
        var changed = controls.ToDictionary(c => c.Id, c =>
        {
            var moved = change(c);
            var width = Math.Min(moved.Width, screen.Width);
            var height = Math.Min(moved.Height, screen.Height);
            return moved with
            {
                Width = width,
                Height = height,
                X = Math.Clamp(moved.X, 0, screen.Width - width),
                Y = Math.Clamp(moved.Y, 0, screen.Height - height),
            };
        });
        var updated = screen.Controls.ConvertAll(c => changed.TryGetValue(c.Id, out var next) ? next : c);
        if (updated.SequenceEqual(screen.Controls))
        {
            return false;
        }

        Commit(Document.WithScreen(screen with { Controls = updated }));
        return true;
    }
}
