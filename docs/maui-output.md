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
| TabControl | A `Grid` drawing a frame under a row of `Button` tabs, with each page a stack layout at the fixed inset (8, 36, 8, 8); clicking a tab shows its page and colours it as chosen |

Each control's name is both its `x:Name` and its `AutomationId`. Text size, bold and colours
become `FontSize`, `FontAttributes` and `TextColor`/`BackgroundColor`.

Hooks are partial methods named after MAUI's events: `OnSaveButtonClicked(EventArgs e)`,
`OnNameTextBoxTextChanged(TextChangedEventArgs e)`, `OnSubscribeCheckBoxCheckedChanged(...)`,
`OnThemeComboBoxSelectedIndexChanged(EventArgs e)`. A Button that opens a screen pushes that
screen's page as a modal page, which fills the window, so its anchored controls move and
stretch to the window's size; one that closes its screen pops it (or closes the window if it
is the first screen).

## Theme

The generated `App.g.cs` sets `UserAppTheme` to `Light` or `Dark`, or to `Unspecified` for
System, which follows the device. The drawn TabControls and group frames use
`AppThemeBinding` colours, so they suit either.
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

The page's `BindingContext` is a new view model, set in the XAML, and its `ViewModel` property
returns it. Values are bound with `{Binding}`; a Picker's or CollectionView's `SelectedItem` is
the chosen item's text. A ProgressBar shows progress from 0 to 1, so a bound one goes through
the generated `RangeToProgressConverter` with the design's minimum and maximum. Types: text is
`string`, on or off `bool`, a number `double`, a choice `string?`, a date `DateTime?`.

A Button's Command field names a method of the view model, such as `Save`. The view model
gets `public void Save()`, which calls a partial `OnSave()` for you to implement in your own part
of the class; the button's generated click handler calls `ViewModel.Save()` after its hook and
before its action (opening or closing a screen). A screen with only commands also has a view
model.

A Button's Enabled when field names an on-or-off property of the view model, such as
`CanSave`: the button is enabled only while it is on. A CheckBox bound to the same name turns it
on and off, or your code can set it; a property only buttons use starts on. The button gets `IsEnabled="{Binding CanSave}"`.

## Tab order

The screen's tab order is not exported: .NET 10's MAUI no longer has `TabIndex`, so Tab
follows the order the controls are in on the page.

## Checks

Unit tests check the XAML and code everywhere. On Windows, CI installs the MAUI workload,
builds and runs both exported samples, and a test reads the controls' positions through UI
Automation on both screens (opening the second with OK and closing it with Close). MAUI
draws its own title bar inside the window, so positions are compared relative to the page.
The window is sized to allow for its frame and MAUI's title bar (measured at 100% scaling on
Windows), so the page gets the design size.
