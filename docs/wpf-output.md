# WPF output

The builder can turn a project into a complete WPF application that builds with
`dotnet build` and opens a window matching the design.

The generator lives in `src/StandaloneUiBuilder.Output.Wpf`. It only produces text, does not
depend on WPF, and never changes the builder project.

## What is generated

Exporting a project named "Customer form" into a folder `C:\Exports` produces
`C:\Exports\CustomerForm\`:

| File | Contents | On re-export |
|---|---|---|
| `CustomerForm.csproj` | A `net10.0-windows` WPF application | Kept |
| `App.xaml`, `App.xaml.cs` | Starts `MainWindow` | Kept |
| `MainWindow.xaml` | The window and every control | **Regenerated** |
| `MainWindow.xaml.cs` | Constructor calling `InitializeComponent()` | Kept |

Only `MainWindow.xaml` is rewritten on later exports. The other files are created once and
then belong to you, so code you add to `MainWindow.xaml.cs` survives design changes. If a
file's content would not change, it is not rewritten.

The namespace comes from the project name ("Customer form" → `CustomerForm`). If the folder
already holds an earlier export, its namespace is kept, so a renamed project still matches
existing code-behind.

## How the design maps to XAML

- The window is titled with the project name, sizes itself to the screen
  (`SizeToContent="WidthAndHeight"`) and cannot be resized, because the layout is absolute.
- Controls go in a `Canvas` the size of the screen, in draw order, using `Canvas.Left`,
  `Canvas.Top`, `Width` and `Height`.
- Each control's name becomes its `x:Name`, so it is a field you can use from code-behind.
- Label, Button and CheckBox text becomes `Content`, TextBox text becomes `Text`, CheckBox
  checked state becomes `IsChecked`, and ComboBox items become `ComboBoxItem`s in order.
- The same padding and alignment the designer uses are written out (Label `Padding="2,0"`,
  vertically centred content), so the window looks like the design surface.
- Text is XML-escaped. Underscores in Label, Button and CheckBox text are doubled so WPF
  shows them rather than treating them as access keys, matching the designer. Text starting
  with `{` is escaped so it is not read as a binding.

Example (`docs/samples/customer-form.uibproj`):

```xml
<Canvas Width="800" Height="600">
    <Label x:Name="TitleLabel" Canvas.Left="40" Canvas.Top="30" Width="300" Height="30" Padding="2,0" VerticalContentAlignment="Center" Content="Customer details" />
    <TextBox x:Name="NameTextBox" Canvas.Left="150" Canvas.Top="80" Width="260" Height="30" VerticalContentAlignment="Center" Text="" />
    <ComboBox x:Name="PlanComboBox" Canvas.Left="150" Canvas.Top="160" Width="160" Height="30" VerticalContentAlignment="Center">
        <ComboBoxItem Content="Basic" />
        ...
    </ComboBox>
    <Button x:Name="SubmitButton" Canvas.Left="150" Canvas.Top="250" Width="120" Height="32" Content="Submit" />
</Canvas>
```

## What stops an export

- A control named with a C# keyword (for example `class`), or with a name that clashes with
  the generated window (`MainWindow`, `InitializeComponent`, `Content`, `Title`, `Width`,
  `Height`, `Name` and similar). The message names the control to rename.
- A `MainWindow.xaml` in the target folder that the builder did not generate. It is never
  overwritten.

## Not generated yet

Event handlers, data binding, styles and resizable layouts. Buttons do nothing until you add
code. Absolute positions suit fixed-size tools and dialogs; responsive layouts need the
layout-model work listed in the spec's "decisions to revisit".
