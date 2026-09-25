# WinForms output

File > Export to WinForms… (Ctrl+Shift+E) turns a design into a complete Windows Forms
application that builds with `dotnet build` or opens in Visual Studio.

The generator lives in `src/StandaloneUiBuilder.Output.WinForms`. It produces text only and
does not depend on WinForms. It shares the export rules in `src/StandaloneUiBuilder.Output`
with the WPF generator.

## What is generated

Exporting "Customer form" into `C:\Exports` produces `C:\Exports\CustomerForm\`:

| File | Contents | On re-export |
|---|---|---|
| `CustomerForm.csproj` | A `net10.0-windows` WinForms application | Kept |
| `Program.cs` | Starts `MainForm` | Kept |
| `MainForm.cs` | Constructor calling `InitializeComponent()`; your code goes here | Kept |
| `MainForm.Designer.cs` | Creates and lays out every control | **Regenerated** |
| `MainForm.Events.g.cs` | Event wiring and hooks | **Regenerated** |
| `SettingsForm.cs`, `.Designer.cs`, `.Events.g.cs` | The same three files for each further screen, named after it | As for `MainForm` |

Each screen becomes its own form. The first screen is always `MainForm`, which `Program`
runs; every other screen is named after itself (Settings becomes `SettingsForm`, titled
"Settings"). A button set in the builder to open it runs, after its hook:

```csharp
using var form = new SettingsForm();
form.ShowDialog(this);
```

and a button set to close its screen calls `Close()`. Your own code can open forms too.

A later screen named `Main` would clash with `MainForm` and blocks the export. Renaming,
reordering or deleting screens after exporting leaves the old screen's files, and any code
in them, for you to move; the builder never deletes files.

`MainForm.Designer.cs` has the shape Visual Studio's form designer writes, so the form opens
in Visual Studio's designer. Change the layout in the builder, though: the builder rewrites
this file on every export. A `MainForm.Designer.cs` the builder did not create is never
overwritten. The namespace of an earlier export is kept, as for WPF.

## How the design maps to WinForms

- Position and size become `Location` and `Size`; anchors become `Anchor` (for example
  `AnchorStyles.Top | AnchorStyles.Right`).
- The form's `ClientSize` is the design size. Sizes are in DIPs: `AutoScaleMode.Dpi` at
  96 DPI scales them for the screen, and the project opts into per-monitor DPI.
- If any control follows the right or bottom edge, the form can be resized but not made
  smaller than the design. Otherwise it has a fixed border and no Maximize button.
- Containers become a `TableLayoutPanel` set up to follow the builder's rules. A StackPanel
  has one column (or row) and a fixed-size row (or column) per child, big enough for the
  child plus the spacing before it, then a filler that takes any remaining space. A Grid has
  a fixed-size (`Absolute`) row or column for each fixed size and a percentage of the space
  left for each share, and a spanning child gets `SetRowSpan`/`SetColumnSpan`.
  Children fill their cell (`Dock = Fill`), with the stack spacing as their leading `Margin`.
- Draw order is kept (WinForms puts the first control added on top, so they are added in
  reverse). Tab order follows the design's control order.
- Label, Button, CheckBox, RadioButton and GroupBox text becomes `Text` with `&` doubled, so
  it shows literally as in the designer. Labels do not auto-size and centre their text
  vertically. Checked states become `Checked`. ComboBox and ListBox items are added in order;
  the ComboBox is a drop-down list, and the ListBox keeps its designed height
  (`IntegralHeight = false`).
- A multi-line TextBox gets `Multiline` and a vertical scroll bar. A PasswordBox is a
  `TextBox` with `UseSystemPasswordChar`. A Slider is a `TrackBar` without ticks that keeps
  its designed height; it and the ProgressBar get `Minimum`, `Maximum` and `Value`. A
  DatePicker is a `DateTimePicker` with a tick box, unticked, meaning no date chosen yet.
- A GroupBox holds a `TableLayoutPanel` called `<Name>Layout`, set 8 pixels in from its
  sides and bottom and 20 from its top and anchored to all four sides. The panel lines up the
  children like a StackPanel's. A control named like that panel blocks the export.
- RadioButtons group by their parent, as in the design.

### Differences from the design surface

WinForms controls look and measure differently from WPF controls:

- A single-line TextBox, PasswordBox, ComboBox or DatePicker takes its height from its font,
  so a height set in the builder is not applied. Position, width and anchors are.
- Fonts, padding and borders are WinForms' own, so text sits slightly differently.
- `TableLayoutPanel` rounds each percentage row and column down and gives the leftover
  pixels to the last one, so a 300-pixel grid with three rows gets 99, 99 and 102 rather than
  100 each. The Windows layout test allows one pixel per row or column inside a grid.
- The layout tests run at 96 DPI. At other DPI settings, fixed `TableLayoutPanel` row sizes
  depend on how the WinForms version scales them.

## Responding to controls

As in WPF output, each control you can interact with has a hook: a partial method you can
implement in the form's `.cs` file.

| Control | Event | Hook |
|---|---|---|
| Button | `Click` | `partial void On<Name>Click(EventArgs e)` |
| CheckBox | `Click` | `partial void On<Name>Click(EventArgs e)` |
| TextBox | `TextChanged` | `partial void On<Name>TextChanged(EventArgs e)` |
| ComboBox | `SelectedIndexChanged` | `partial void On<Name>SelectedIndexChanged(EventArgs e)` |
| RadioButton | `Click` | `partial void On<Name>Click(EventArgs e)` |
| ListBox | `SelectedIndexChanged` | `partial void On<Name>SelectedIndexChanged(EventArgs e)` |
| Slider (`TrackBar`) | `ValueChanged` | `partial void On<Name>ValueChanged(EventArgs e)` |
| DatePicker (`DateTimePicker`) | `ValueChanged` | `partial void On<Name>ValueChanged(EventArgs e)` |
| PasswordBox (`TextBox`) | `TextChanged` | `partial void On<Name>TextChanged(EventArgs e)` |

```csharp
partial void OnSubmitButtonClick(EventArgs e)
{
    MessageBox.Show($"Thanks, {NameTextBox.Text}");
}
```

## Names

A control named with a C# keyword, the form's class name, or a Form member the generated code
uses (`Text`, `Controls`, `Name`, `ClientSize` and a few more) blocks the export with a
message. A control named after another common Form member, such as `CancelButton` or
`AcceptButton`, is declared with `new`, which says the hiding is intended. Your code then
refers to the control, not the Form property.

## Checks

- Unit tests check the generated code's content and parse every generated file with the C#
  compiler (Roslyn) for syntax errors.
- On Windows, a test builds the exported samples (including `layout-demo`, with nested
  containers), runs them, and reads each control's real
  position and size through UI Automation. It then enlarges the window and checks that
  anchored controls moved and stretched as the Core anchor rules say, and that an
  implemented Click hook runs. For the layout demo, the OK button's action opens its second
  screen's form, whose layout is checked the same way, and that form's Close button closes it.
- CI builds and runs the exported sample and captures a screenshot.
