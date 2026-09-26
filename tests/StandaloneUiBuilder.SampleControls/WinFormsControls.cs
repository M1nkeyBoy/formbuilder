using System.ComponentModel;

namespace SampleControls.WinForms;

public enum Direction
{
    Across,
    Down,
}

/// <summary>A Windows Forms control; Text comes from Control.</summary>
public class Meter : System.Windows.Forms.Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int Level { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Direction Direction { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal Zoom { get; set; }
}

/// <summary>Not a control: a form.</summary>
public class SampleForm : System.Windows.Forms.Form
{
}
