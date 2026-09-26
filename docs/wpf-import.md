# Importing from WPF

File > Import from WPF… starts a new project from WPF windows. Choose one or more window XAML
files (a WPF project's `MainWindow.xaml`, `SettingsWindow.xaml` and so on); each becomes a
screen, with `MainWindow` first. The project is not saved until you save it, and the XAML
files are only read.

Windows the builder exported come back as they were designed, including buttons that open
or close screens (read from the `.Events.g.cs` file beside each window) and pictures (read
from the `Assets` folder). Other WPF windows are read as far as the builder can represent
them; everything else is left out and listed when the import finishes.

## What is read

- **The screen size:** the root panel's `Width` and `Height` (or `MinWidth` and `MinHeight`),
  otherwise the window's size, which includes its frame.
- **Positions:** controls in a root `Grid` without rows or columns, or in a `Canvas`. A Grid
  child's alignment and margins become its anchors (Left, Right or both; Top, Bottom or
  both); a centred control keeps its place from the left or top instead. `Canvas.Left`,
  `Top`, `Right` and `Bottom` work the same way. Any other root panel becomes one container
  filling the screen.
- **Controls:** Label (and TextBlock, as a Label), Button, TextBox (multi-line with
  `AcceptsReturn`), PasswordBox, CheckBox, RadioButton, ComboBox and ListBox with their items,
  Slider and ProgressBar with their range, DatePicker, and Image with its picture file.
- **Containers:** StackPanel (the gap before each child becomes the spacing), Grid with its
  rows, columns and spans (`Auto` sizes become equal shares), GroupBox holding a
  StackPanel, and TabControl: each `TabItem` becomes a page with its `Header`, and its content
  the page's controls (a StackPanel's children, or the one control). `SelectedIndex` is kept.
  The builder's own exported TabControls read back exactly.
- **Text and style:** content and text (with WPF's `_` access-key markers removed), checked
  states, `FontSize`, bold weights, and `#RRGGBB` or opaque `#AARRGGBB` colours.
- **Names:** each `x:Name`; controls without one get the builder's usual names.
- **Tab order:** `TabIndex` on named controls. Controls with one come first, lowest first, as
  WPF orders them, and the rest follow in their order.
- **Theme:** the main window's `ThemeMode`: `Dark` and `System` become the project's theme,
  anything else Light. (Text colours the builder added for contrast read back as the
  controls' own.)

## What is left out

Anything else: other controls (Expander, DataGrid, Menu…), DockPanel and
WrapPanel inside the window, named colours and brushes other than plain colours, styles and
resources, data binding, and your code. Each is listed with its name.
