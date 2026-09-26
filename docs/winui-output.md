# WinUI 3 output

File > Export to WinUI 3… turns the design into a WinUI 3 desktop app built on the Windows App
SDK (1.8). It is unpackaged and self-contained: `dotnet build` produces a program that runs
from its build folder, with no installer and no separately installed Windows App SDK runtime.
Open the `.csproj` in Visual Studio (with the WinUI application development workload) or run
`dotnet build` on Windows. Like the other outputs, it is text generated from the design.

## What is generated

Exporting "Layout demo" into `C:\Exports` produces `C:\Exports\LayoutDemo\`:

| File | Contents | On re-export |
|---|---|---|
| `LayoutDemo.csproj`, `app.manifest` | A `net10.0-windows10.0.19041.0` WinUI 3 app, per-monitor DPI aware | Kept |
| `App.xaml`, `App.xaml.cs` | Opens `MainWindow` | Kept |
| `MainWindow.xaml` | The window and every control | **Regenerated** |
| `MainWindow.g.cs` | Title, design size, event wiring and hooks | **Regenerated** |
| `MainWindow.xaml.cs` | Constructor; your code goes here | Kept |
| `SettingsWindow.*` | The same for each further screen | As above |
| `Assets/<screen>/<control>.png` | Pictures from Image controls | **Regenerated** |

The project builds for the computer's own processor (x64 or ARM64) unless another runtime
identifier is given.

## How the design maps to WinUI

The layout is the same as the WPF output's: a root `Grid` with each control placed by
alignment and margins from its anchors, `StackPanel` and `Grid` containers with the same
rows, columns and spans, and the window opening at the design size (made resizable only if
some control follows the right or bottom edge). The differences come from WinUI itself:

- WinUI has no **Label**; a Label is a `ContentControl`, which centres its text the same way.
- WinUI has no **GroupBox**; a GroupBox is a `Grid`, named after the design, drawing a rounded
  frame and the title, with a `StackPanel` for the children at the fixed inset.
- WinUI's `TabView` is for documents (closable tabs, an add button), so a **TabControl** is
  drawn like the GroupBox: a `Grid`, named after the design, with a frame under a row of
  `ToggleButton` tabs and each page as a `StackPanel` at the fixed inset (8, 36, 8, 8). The
  tabs' `Click` handler checks the clicked tab only, shows its page and calls the hook.
- A **DatePicker** is a `CalendarDatePicker`, the text box with a drop-down calendar that
  matches the other outputs; its hook is `DateChanged`.
- WinUI's default styles give many controls a minimum width and height (a CheckBox is at
  least 120 wide). Every control sets `MinWidth="0" MinHeight="0"`, so the design's size wins.
- The window is sized in code (`AppWindow.ResizeClient`), scaled for the display, since a
  WinUI window has no size in XAML.
- Pictures are copied next to the program and shown with `ms-appx:///Assets/...`.
- A Button that opens a screen shows that screen's window with `Activate()` (WinUI has no
  modal windows); one that closes its screen calls `Close()`.

Hooks work as in the WPF output: implement `partial void OnSaveButtonClick(RoutedEventArgs e)`
in the window's `.xaml.cs` file.

## Theme

A Light or Dark project sets `RequestedTheme` on each window's content; a System project
leaves it out, so windows follow Windows' app mode.
On a background the design sets (a control's own, or its container's), text gets black or
white, whichever stands out, unless it has a text colour of its own, and controls inside such
a container take its background; so the design's own colours read the same in a dark theme.

## Data binding

A screen whose controls are bound (see the editor's Binding field) gets a view model,
`{Screen}ViewModel.g.cs` (`MainViewModel` for the first screen), regenerated on every export: a
partial class with one property per binding name, starting from the first bound control's
design value, that raises `PropertyChanged` when it changes and calls a partial
`On{Name}Changed()` hook. Add your own members in another part of the class. Controls that share
a name share the value: in the layout demo, moving the slider moves the progress bar.

The window's `ViewModel` property holds it, and values are bound with `x:Bind`, two way (a
TextBox's text as you type), except a Label's and ProgressBar's, which only show it. Types
follow the controls, as `x:Bind` needs: text is `string`, on or off `bool?`, a number `double`,
a choice `string?` (the chosen item's text: shown through `SelectedValue`, and written back by
the selection handler, since `x:Bind` cannot turn an item into text), a date `DateTimeOffset?`.

A Button's Command field names a method of the view model, such as `Save`. The view model
gets `public void Save()`, which calls a partial `OnSave()` for you to implement in your own part
of the class; the button's generated click handler calls `ViewModel.Save()` after its hook and
before its action (opening or closing a screen). A screen with only commands also has a view
model.

## Tab order

When the screen has a tab order of its own, each control Tab visits gets its place in it as
`TabIndex`, which WinUI compares across the window. A TabControl's tab buttons share its place.

## Checks

- Unit tests parse every generated window as XAML, check the layout attributes and the
  generated code, and parse the C# with Roslyn.
- On Windows, a test builds the exported layout demo, runs it, and reads each control's real
  position and size through UI Automation on both screens (opening the second with the OK
  button and closing it with Close). Labels and panels have no UI Automation element in
  WinUI, so the controls inside them are what is measured. CI also builds and runs both
  exported samples.
