using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SampleControls.Wpf;

/// <summary>How a rating is drawn.</summary>
public enum RatingShape
{
    Star,
    Heart,
    Circle,
}

/// <summary>Flags are not offered: the builder edits one choice at a time.</summary>
[Flags]
public enum RatingParts
{
    None = 0,
    Label = 1,
    Value = 2,
}

/// <summary>A control with the kinds of properties the builder can set.</summary>
public class Rating : Control
{
    public string Caption { get; set; } = "";

    public int Stars { get; set; } = 5;

    public double Value { get; set; }

    public bool? ShowValue { get; set; }

    public RatingShape Shape { get; set; }

    public RatingParts Parts { get; set; }

    public float Spacing { get; set; }

    public long Votes { get; set; }

    /// <summary>Read-only: not offered.</summary>
    public int Total => Stars;

    /// <summary>Hidden from designers: not offered.</summary>
    [Browsable(false)]
    public string Secret { get; set; } = "";

    /// <summary>Not a simple type: not offered.</summary>
    public Thickness Gap { get; set; }
}

/// <summary>Derives from another library control, so it has its properties too, and ContentControl's Content.</summary>
public class Badge : ContentControl
{
    public RatingShape Shape { get; set; }
}

/// <summary>A library control deriving from another library control.</summary>
public class BigRating : Rating
{
    public int Rows { get; set; }
}

/// <summary>Abstract: not a control to place.</summary>
public abstract class RatingBase : Control
{
}

/// <summary>No constructor without arguments: cannot be placed.</summary>
public class NeedsArgument : Control
{
    public NeedsArgument(int size) => Width = size;
}

/// <summary>Hidden from the toolbox.</summary>
[ToolboxItem(false)]
public class HiddenControl : Control
{
}

/// <summary>A part of another control, by its name.</summary>
public class RatingItem : Control
{
}

/// <summary>Not a control: a template selector in WPF's controls namespace.</summary>
public class ShapeSelector : DataTemplateSelector
{
}

/// <summary>A window is not placed on a screen.</summary>
public class SampleWindow : Window
{
}

/// <summary>Internal: not offered.</summary>
internal class InternalControl : Control
{
}
