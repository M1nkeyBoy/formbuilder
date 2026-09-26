using Microsoft.AspNetCore.Components;

namespace SampleControls.Blazor;

public enum Tone
{
    Plain,
    Loud,
}

/// <summary>A component: its parameters are what the builder sets.</summary>
public class Banner : ComponentBase
{
    [Parameter]
    public string Heading { get; set; } = "";

    [Parameter]
    public Tone Tone { get; set; }

    [Parameter]
    public bool Closable { get; set; }

    /// <summary>Not a parameter: not offered.</summary>
    public string Internal { get; set; } = "";
}

/// <summary>A generic component: its type argument is set too.</summary>
public class Picker<TValue> : ComponentBase
{
    [Parameter]
    public TValue? Value { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = "";
}

/// <summary>A layout is not a component to place.</summary>
public class SampleLayout : LayoutComponentBase
{
}
