# .NET MAUI output

File > Export to .NET MAUI… turns the design into a .NET MAUI app: a `ContentPage` per screen,
opened in the app's window with the first screen. The project targets Windows, where the
builder builds and checks it (unpackaged and self-contained, like the WinUI 3 output); add
`net10.0-android` and the other platforms to its `TargetFrameworks` to build for them. Building
needs the .NET MAUI workload (`dotnet workload install maui-windows`, or Visual Studio's .NET
MAUI workload).

## What is generated

| File | Contents | On re-export |
|---|---|---|
| `<Name>.csproj`, `MauiProgram.cs`, `App.xaml`, `App.xaml.cs` | The app | Kept |
| `App.g.cs` | The window: title and size from the first screen | **Regenerated** |
| `Platforms/Windows/*` | The Windows entry point and manifests | Kept |
| `MainPage.xaml` | The first screen's page | **Regenerated** |
| `MainPage.g.cs` | Its event wiring and hooks | **Regenerated** |
| `MainPage.xaml.cs` | Your code for the page | Kept |
| `SettingsPage.*` | The same for each further screen | As above |
| `Resources/Images/<screen>_<control>.png` | Pictures from Image controls | **Regenerated** |

## How the design maps to MAUI

Layout follows the other XAML outputs: a root `Grid` whose children have
`HorizontalOptions`/`VerticalOptions` (Start, End or Fill) and margins from their anchors,
`WidthRequest` and `HeightRequest` for their size, `VerticalStackLayout` or
`HorizontalStackLayout` (with `Spacing`) for a StackPanel, and a `Grid` with the same
`RowDefinitions` and `ColumnDefinitions` for a Grid. Controls map to MAUI's own:

| Design | MAUI |
|---|---|
| Label, Button, RadioButton, Slider, ProgressBar, DatePicker, Image | The control of the same name (a ProgressBar's value becomes `Progress` from 0 to 1) |
| TextBox, PasswordBox | `Entry` (with `IsPassword`), or `Editor` when multi-line |
| CheckBox | A `CheckBox` beside a `Label`, in a small Grid that takes its place, since a MAUI CheckBox has no text |
| ComboBox | `Picker` with its items |
| ListBox | `CollectionView` with its items, single selection |
| GroupBox | A `Grid` drawing a frame and title, with a stack layout at the fixed inset |

Each control's name is both its `x:Name` and its `AutomationId`. Text size, bold and colours
become `FontSize`, `FontAttributes` and `TextColor`/`BackgroundColor`.

Hooks are partial methods named after MAUI's events: `OnSaveButtonClicked(EventArgs e)`,
`OnNameTextBoxTextChanged(TextChangedEventArgs e)`, `OnSubscribeCheckBoxCheckedChanged(...)`,
`OnThemeComboBoxSelectedIndexChanged(EventArgs e)`. A Button that opens a screen pushes that
screen's page as a modal page; one that closes its screen pops it (or closes the window if it
is the first screen).

## Checks

Unit tests check the XAML and code everywhere. On Windows, CI installs the MAUI workload,
builds and runs both exported samples, and a test reads the controls' positions through UI
Automation on both screens (opening the second with OK and closing it with Close). MAUI
draws its own title bar inside the window, so positions are compared relative to the page.
The window is sized to allow for its frame and MAUI's title bar (measured at 100% scaling on
Windows), so the page gets the design size.
