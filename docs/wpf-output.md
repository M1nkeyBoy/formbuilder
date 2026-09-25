# WPF output

The builder can turn a project into a complete WPF application that builds with
`dotnet build` and opens a window matching the design.

The generator lives in `src/StandaloneUiBuilder.Output.Wpf`. It only produces text, does not
depend on WPF, and never changes the builder project.

![The sample project exported and running as its own WPF application](screenshots/generated-wpf-app.png)

*The sample project after export, built with `dotnet build` and running on Windows (captured
by CI).*

## What is generated

Exporting a project named "Customer form" into a folder `C:\Exports` produces
`C:\Exports\CustomerForm\`:

| File | Contents | On re-export |
|---|---|---|
| `CustomerForm.csproj` | A `net10.0-windows` WPF application | Kept |
| `App.xaml`, `App.xaml.cs` | Starts `MainWindow` | Kept |
| `MainWindow.xaml` | The window and every control | **Regenerated** |
| `MainWindow.xaml.cs` | Constructor calling `InitializeComponent()` | Kept |
| `MainWindow.Events.g.cs` | Event wiring and hooks (below) | **Regenerated** |

Only `MainWindow.xaml` and `MainWindow.Events.g.cs` are rewritten on later exports. The other files are created once and
then belong to you, so code you add to `MainWindow.xaml.cs` survives design changes. If a
file's content would not change, it is not rewritten.

The namespace comes from the project name ("Customer form" → `CustomerForm`). If the folder
already holds an earlier export, its namespace is kept, so a renamed project still matches
existing code-behind.

## How the design maps to XAML

- The window is titled with the project name and opens at the design size
  (`SizeToContent="WidthAndHeight"`).
- Controls go in a `Grid`, in draw order. Each control's anchors become
  `HorizontalAlignment`, `VerticalAlignment`, `Margin` and, where it does not stretch, `Width`
  and `Height`:

  | Anchor (one axis) | Alignment | Margin | Size |
  |---|---|---|---|
  | left only | `Left` | distance from left | fixed |
  | right only | `Right` | distance from right | fixed |
  | left and right | `Stretch` | both distances | stretches |

  Top and bottom work the same way vertically.
- If any control is anchored to the right or bottom edge, the window can be resized
  (`ResizeMode="CanResize"`) and the Grid has the design size as its minimum. Otherwise the
  window keeps the design size and can only be minimised.
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
<Grid MinWidth="800" MinHeight="600">
    <Label x:Name="NameLabel" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="40,80,0,0" Width="100" Height="30" Padding="2,0" VerticalContentAlignment="Center" Content="Full name" />
    <TextBox x:Name="NameTextBox" HorizontalAlignment="Stretch" VerticalAlignment="Top" Margin="150,80,390,0" Height="30" VerticalContentAlignment="Center" Text="" TextChanged="NameTextBox_TextChanged" />
    <ComboBox x:Name="PlanComboBox" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="150,160,0,0" Width="160" Height="30" VerticalContentAlignment="Center" SelectionChanged="PlanComboBox_SelectionChanged">
        <ComboBoxItem Content="Basic" />
        ...
    </ComboBox>
    <Button x:Name="SubmitButton" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,250,530,0" Width="120" Height="32" Content="Submit" Click="SubmitButton_Click" />
</Grid>
```

## Responding to controls

Every Button, CheckBox, TextBox and ComboBox is wired to one event, and each has a *hook*: a
partial method you can implement in `MainWindow.xaml.cs`.

| Control | Event | Hook to implement |
|---|---|---|
| Button | `Click` | `partial void On<Name>Click(RoutedEventArgs e)` |
| CheckBox | `Click` | `partial void On<Name>Click(RoutedEventArgs e)` |
| TextBox | `TextChanged` | `partial void On<Name>TextChanged(TextChangedEventArgs e)` |
| ComboBox | `SelectionChanged` | `partial void On<Name>SelectionChanged(SelectionChangedEventArgs e)` |

For example, in `MainWindow.xaml.cs`:

```csharp
partial void OnSubmitButtonClick(RoutedEventArgs e)
{
    MessageBox.Show($"Thanks, {NameTextBox.Text}");
}
```

The XAML refers to a private handler such as `SubmitButton_Click` in the regenerated
`MainWindow.Events.g.cs`, which calls the hook. Hooks you do not implement compile to
nothing, so adding controls in the designer never breaks the build. If you remove or rename a
control whose hook you implemented, the compiler reports that hook as having no declaration,
which tells you exactly which method to move or delete.

Note that WPF raises `TextChanged` while the window loads when a TextBox has initial text,
before later controls exist.

## What stops an export

- A control named with a C# keyword (for example `class`), or with a name that clashes with
  the generated window (`MainWindow`, `InitializeComponent`, `Content`, `Title`, `Width`,
  `Height`, `Name` and similar) or with another control's generated handler or hook (a Label
  called `OnSaveClick` next to a Button called `Save`). The message names the control to
  rename.
- A `MainWindow.xaml` in the target folder that the builder did not generate. It is never
  overwritten.

## Not generated yet

Data binding, styles, and nested layout containers such as stacks and grids. Absolute positions suit fixed-size tools and dialogs; responsive layouts need the
layout-model work listed in the spec's "decisions to revisit".
