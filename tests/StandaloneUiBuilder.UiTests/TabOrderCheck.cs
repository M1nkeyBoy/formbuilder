using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Presses Tab through a running window and checks it visits controls in the design's tab order.</summary>
internal static class TabOrderCheck
{
    /// <summary>
    /// Radio buttons are left out (some targets stop only at the chosen one of a group), as are
    /// controls on hidden tab pages and any the target gives no automation ID of their own.
    /// </summary>
    /// <param name="window">Focused first, unless <paramref name="focusWindow"/> is false (focus is already inside it).</param>
    public static void AssertTabOrder(AutomationBase automation, AutomationElement window, ScreenDocument screen, Func<ControlDocument, bool>? leaveOut = null, bool focusWindow = true)
    {
        var hidden = ContainerLayout.Flatten(screen).Where(p => p.IsHidden).Select(p => p.Control.Id).ToHashSet();
        var expected = TabSequence.Resolve(screen)
            .Where(c => c.Type != Core.ControlType.RadioButton && !hidden.Contains(c.Id) && leaveOut?.Invoke(c) != true)
            .Select(c => c.Name)
            .ToList();
        var names = expected.ToHashSet();
        var walker = automation.TreeWalkerFactory.GetControlViewWalker();

        if (focusWindow)
        {
            window.Focus();
            Wait.UntilInputIsProcessed();
        }

        var visited = new List<string>();
        var trail = new List<string>();

        // Done once the first control has been reached and every control after it visited.
        bool RoundDone()
        {
            var first = visited.IndexOf(expected[0]);
            return first >= 0 && visited.Count - first >= expected.Count;
        }

        // Enough presses to go round twice, with room for other stops (the editor's own panels).
        for (var i = 0; i < expected.Count * 4 + 40 && !RoundDone(); i++)
        {
            Keyboard.Type(VirtualKeyShort.TAB);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(150);

            // The control is the focused element or the nearest element around it with a design name.
            string? name = null;
            for (var element = automation.FocusedElement(); element is not null && name is null; element = walker.GetParent(element))
            {
                var id = element.Properties.AutomationId.ValueOrDefault;
                name = id is not null && names.Contains(id) ? id : null;
            }

            trail.Add(name ?? "?");
            if (name is not null && (visited.Count == 0 || visited[^1] != name))
            {
                visited.Add(name);
            }
        }

        var start = visited.IndexOf(expected[0]);
        Assert.True(start >= 0 && visited.Count >= start + expected.Count,
            $"{screen.Name}: Tab visited {string.Join(", ", trail)}; expected {string.Join(", ", expected)}");
        Assert.True(expected.SequenceEqual(visited.Skip(start).Take(expected.Count)),
            $"{screen.Name}: Tab visited {string.Join(", ", visited.Skip(start))}; expected {string.Join(", ", expected)}");
    }
}
