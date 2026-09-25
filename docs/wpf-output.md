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

Data binding, styles and resizable layouts. Absolute positions suit fixed-size tools and dialogs; responsive layouts need the
layout-model work listed in the spec's "decisions to revisit".
