using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>For tests of what unbound controls produce: the sample's layout demo binds its Settings values and runs a command.</summary>
internal static class Unbound
{
    /// <summary>The document with every binding and command removed.</summary>
    public static ProjectDocument Of(ProjectDocument document) => document with
    {
        Screens = document.Screens.ConvertAll(s => s with { Controls = s.Controls.ConvertAll(Strip) }),
    };

    private static ControlDocument Strip(ControlDocument control) => control with
    {
        Properties = control.Properties with { Binding = null, Command = null, EnabledBinding = null },
        Children = control.Children?.ConvertAll(Strip),
    };
}
