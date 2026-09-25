namespace StandaloneUiBuilder.Core;

/// <summary>The built-in control types a screen can contain. Persisted by name.</summary>
public enum ControlType
{
    Label,
    Button,
    TextBox,
    CheckBox,
    ComboBox,

    /// <summary>One choice among several: the RadioButtons in the same container exclude each other.</summary>
    RadioButton,

    /// <summary>A list of text items, all visible, one of which can be selected.</summary>
    ListBox,

    /// <summary>A whole number picked by dragging a thumb between a minimum and a maximum.</summary>
    Slider,

    /// <summary>Shows a whole number between a minimum and a maximum as a filled bar.</summary>
    ProgressBar,

    /// <summary>A date typed or picked from a calendar. It starts with no date chosen.</summary>
    DatePicker,

    /// <summary>A single-line text box that hides what is typed.</summary>
    PasswordBox,

    /// <summary>A container that lines its children up vertically or horizontally.</summary>
    StackPanel,

    /// <summary>A container with rows and columns; each child fills one or more cells.</summary>
    Grid,

    /// <summary>
    /// A titled frame that lines its children up like a StackPanel, inside a fixed inset
    /// (<see cref="ContainerLayout.GroupBoxInset"/>) that leaves room for the frame and title.
    /// </summary>
    GroupBox,
}

/// <summary>The direction a StackPanel lines up its children.</summary>
public enum StackOrientation
{
    Vertical,
    Horizontal,
}
