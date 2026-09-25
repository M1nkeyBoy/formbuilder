namespace StandaloneUiBuilder.Core;

/// <summary>The built-in control types a screen can contain. Persisted by name.</summary>
public enum ControlType
{
    Label,
    Button,
    TextBox,
    CheckBox,
    ComboBox,

    /// <summary>A container that lines its children up vertically or horizontally.</summary>
    StackPanel,

    /// <summary>A container with equal rows and columns; each child fills one cell.</summary>
    Grid,
}

/// <summary>The direction a StackPanel lines up its children.</summary>
public enum StackOrientation
{
    Vertical,
    Horizontal,
}
