# Decisions

Choices made during the prototype slices that affect later work.

## Slice 0

- **SDK pin.** `global.json` requires the .NET 10 SDK, 10.0.100 or a later 10.0 feature band.
- **Solution format.** Classic `.sln`, as the spec names it, rather than the newer `.slnx` default.
- **Cross-platform build.** The WPF project sets `EnableWindowsTargeting` so it compiles on
  Linux and macOS build agents. The editor still runs only on Windows.
- **Test packages.** `xunit`, `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` are the
  only added packages. They are needed to run the Core tests and ship in no product binary.
  The template's `coverlet.collector` was removed because nothing uses coverage yet.
- **Placeholder menus.** File and Edit commands are shown but disabled until the slice that
  implements them. The Design/Preview toggle switches state and the status text only;
  preview behavior arrives in Slice 5.
- **Visual verification.** Development happens on Linux, where WPF cannot launch. A Windows
  CI job builds, tests, launches the app and captures a screenshot for each pushed commit.

## Slice 1

- **Immutable document.** `ProjectDocument`, `ScreenDocument`, `ControlDocument` and
  `ControlProperties` are immutable records. Every edit produces a new document, so undo
  can keep snapshots and "unsaved changes" is a reference comparison with the last saved
  snapshot.
- **Whole-number geometry.** X, Y, Width and Height are integers in DIPs.
- **Control catalog.** `ControlCatalog` in Core lists each type's default size, minimum size
  and supported properties. The WPF side maps the same types to preview controls.
- **Names.** Control names must look like identifiers (letter or underscore first, then
  letters, digits or underscores) and are unique ignoring case, so later code output can use
  them directly.
- **Grid.** The grid is drawn by a non-hit-testable element, with a slightly darker line
  every 50 DIPs.

## Slice 2

- **Placement.** A dropped control's top-left corner goes where the pointer was, snapped to
  the nearest grid line and moved inside the screen if needed. Controls get the lowest free
  default name for their type (`Button1`, `Button2`, …) and default text equal to the name.
- **Design hit testing.** In Design mode each control sits in a transparent host that takes
  the pointer, and the control itself does not respond. The canvas is rebuilt from the
  document after every change, so the document stays the only source of truth.
- **Keyboard placement.** Selecting a toolbox item and pressing Enter adds the control near
  the top-left, stepping 20 DIPs for each control so new ones do not stack exactly.

## Slice 3

- **One undo step per gesture.** While a control is dragged or resized only its visuals
  move; the document changes once, when the mouse button is released. Esc or losing the
  mouse capture cancels the gesture.
- **Undo history.** The editor keeps snapshots of the immutable document. Edits that change
  nothing, and rejected edits, record nothing. Opening or starting a document clears history.
- **Resize handles.** Handles draw at 8 DIPs but respond within 12 DIPs. Middle-of-edge
  handles are hidden when that side is shorter than 40 DIPs, so small controls can still be
  grabbed to move them.
- **Inspector commits.** A field applies on Enter or when it loses focus; Esc restores the
  stored value. Rejected input stays in the field with a red border and a message and is
  never written to the document. Position and size typed here are exact, not snapped.
- **ComboBox items.** Edited one per line; blank lines are dropped and order is kept.

## Slice 4

- **File format.** Recorded in `project-format.md`, with a sample in `samples/`. Field names
  match the spec's example. Unsupported properties are dropped on load and missing ones get
  neutral defaults, so hand-edited files stay usable.
- **Project name.** The title shows the file name. On save, the file name (without
  extension) is also written as the project's `name`.
- **Atomic save.** Write a temporary file in the destination folder, flush it to disk, then
  move it over the destination.
- **Pending input.** A value still being typed in the inspector is applied before Save,
  Open, New or close.
- **Command-line open.** The first command-line argument is opened as a project at startup.
  CI uses this to screenshot the sample project.

## Slice 5

- **Preview isolation.** Preview builds fresh, interactive WPF controls from the document.
  Nothing typed, checked or selected in them is written back; switching to Design rebuilds
  the canvas from the document, which restores the design values. The toolbox, inspector,
  Delete, Undo and Redo are disabled in Preview.
- **Recovery location and format.** Drafts are JSON files in
  `%LOCALAPPDATA%\StandaloneUiBuilder\Recovery`, separate from `.uibproj` files. A draft
  wraps the project JSON with the original project path and the time it was written.
- **Detecting an abnormal exit.** Each running editor holds an exclusive, delete-on-close
  lock file. A draft whose lock is not held was left by a session that ended without
  cleaning up. This also keeps two editors running at once from offering each other's drafts.
- **When drafts are written and removed.** Written two seconds after the last edit while the
  project has unsaved changes; removed whenever it becomes clean (saved, discarded, opened,
  or undone back to the saved state) and on normal close.
- **Recovering.** The newest orphaned draft is offered at startup. Recovering opens it as
  unsaved work linked to the original path; nothing is written to that file until the user
  saves. Declining deletes the draft and leaves the saved file untouched. Older drafts, if
  any, are offered on later starts.

## Slice 6

- **Automated acceptance walkthrough.** Development happens where WPF cannot run, so the
  spec's manual walkthrough is also automated: `tests/StandaloneUiBuilder.UiTests` drives
  the real editor through Windows UI Automation using FlaUI (`FlaUI.UIA3`). That package is
  used only by this test project and ships in no product binary. The test is opt-in
  (`UIB_RUN_UI_TESTS=1`) because it takes over the mouse and keyboard; CI always runs it.
- **Release build.** A self-contained, single-file `win-x64` publish, built and started in
  CI on every push and uploaded as an artifact.
- **Accessibility.** Toolbox items expose their control type as their accessible name.

## Prototype completion checks

- **Responsiveness.** A UI test opens a 61-control screen and selects, moves, resizes and
  places controls. On the CI runner: select 481 ms, place 891 ms, move 2.9 s, resize
  3.2 s. Every figure includes the test's own scripted pointer movement and polling (about
  1 s per drag), so they are upper bounds, not rendering times. No slowdown was visible.
- **Keyboard access.** A UI test reaches the toolbox with Tab, adds a control with the arrow
  keys and Enter, tabs on to the Name field and renames it, and checks that Delete in a
  text field edits the text rather than deleting the control.
- **Display scaling.** The automated tests assume 100% scaling. 150% was checked by hand on
  a real machine, along with the self-contained build running without .NET installed.
- **Test isolation.** `UIB_RECOVERY_DIR` points recovery drafts at another folder, so UI
  tests that end the editor abruptly cannot affect each other or a user's drafts.

## Slice 7 — WPF output generator

- **First output target: WPF**, chosen after prototype review. It is the same stack as the
  editor, so what the designer shows is what the generated app shows.
- **Separate project.** `StandaloneUiBuilder.Output.Wpf` depends only on Core and produces
  text, so it builds and is tested anywhere. Core stays free of any output target.
- **Ownership of generated files.** Only `MainWindow.xaml` is regenerated. Project, App and
  code-behind files are created once and then belong to the developer. A `MainWindow.xaml`
  without the builder's marker comment is never overwritten.
- **Stable namespace.** Re-exporting into a folder reuses the namespace found in its
  `MainWindow.xaml.cs`, so renaming a project cannot break existing code-behind.
- **Names.** C# keywords and names that clash with the generated window's members block
  export with a message, rather than being silently renamed. Designer name rules are
  unchanged, so existing project files stay valid.
- **Visual parity.** The generator writes the same padding and alignment the designer's
  `ControlFactory` sets. Both places carry a comment saying so, and Slice 8 adds a Windows test
  that compares them.

## Slice 9 — Export command

- **File > Export to WPF… (Ctrl+E).** The user picks a parent folder; the project goes in a
  subfolder named after its namespace. The editor remembers the last folder for the session
  only, so the project file format is unchanged.
- **Project name.** Export uses the name shown in the title bar (the file name once saved).
- **Result summary.** After exporting, the editor lists which files were created, updated or
  left unchanged and offers to open the folder.

## Slice 10 — Event hooks in WPF output

- **No project format change.** Hooks are derived from control names and types, so nothing new
  is stored in `.uibproj` files. Choosing which controls get handlers can come later with
  actions.
- **Partial-method hooks.** The regenerated `MainWindow.Events.g.cs` holds the handler the
  XAML names and declares a partial method; the developer implements it in their own file.
  Unimplemented hooks compile away, so the design can change freely without breaking the
  build, and a stale implementation fails loudly with a clear compiler message.
- **One event per type.** Button and CheckBox: Click. TextBox: TextChanged. ComboBox:
  SelectionChanged. Label: none.

## Slice 11 — Anchoring (first layout model)

- **Anchors before containers.** The spec's first decision to revisit asks for a layout model
  beyond absolute positions. Anchors (left, top, right, bottom per control) are the smallest
  model that makes windows resizable. They map directly onto WPF (alignment plus margins)
  and WinForms (`Anchor`), and they keep editing on the same absolute canvas. Nested
  containers (stacks, grids) remain a possible later step.
- **Format version 2.** `anchor` is a new control field, so the schema version went to 2 and
  the change and its migration are recorded in `project-format.md`. Version 1 files load
  with left and top anchors, which reproduces how they behaved. Bumping the version, rather
  than adding an optional field silently, stops an older builder from opening a newer file
  and dropping its anchors on save.
- **Anchors are a list of edge names** (`["left", "top"]`) for readability. Each control
  needs one horizontal and one vertical anchor; the editor refuses any other combination
  and explains why.
- **One set of rules everywhere.** `AnchorLayout` in Core defines where a control goes at
  any window size. The generated WPF window and the editor's Preview both lay out from it,
  and a Windows test checks all three agree at the design size and at a larger size.
- **Resizable only when it matters.** A generated window can be resized only if some control
  follows the right or bottom edge, and never below the design size. Otherwise it keeps the
  fixed design size, as before.
- **Preview resizing.** In Preview a grip at the bottom-right corner enlarges the surface
  (never below the design size) to try the anchors. Design mode stays at the design size.
- **Generated XAML changed from Canvas to Grid** for every design, so that one form handles
  all anchors. Existing exports pick this up on the next export; control names, and
  therefore developers' code, are unaffected.

## Slice 12 — WinForms output

- **Second target: WinForms**, using the same approach as WPF: a text-only generator,
  regenerated layout and event files, developer-owned form and program files, and
  partial-method hooks.
- **Shared export rules.** Naming (`CodeNames`) and writing a project folder
  (`ProjectExporter`: marker check, keep-or-regenerate, stable namespace) moved into
  `StandaloneUiBuilder.Output`, used by both generators.
- **Visual Studio's designer shape.** `MainForm.Designer.cs` uses fully qualified type names
  and `this.` member access, as the Visual Studio designer does, so the form opens there and
  a control named like a type (for example `Size`) cannot break the code.
- **DPI.** Designs are in DIPs, so the form uses `AutoScaleMode.Dpi` at 96 DPI and the project
  opts into per-monitor DPI.
- **Known differences** are documented rather than worked around: single-line TextBox and
  ComboBox heights follow the font in WinForms.
- **Hidden members.** Controls named after common Form members (`CancelButton`) are declared
  `new` to keep the build free of warnings; names the generated code itself uses are
  refused.
- **Syntax checks.** The unit tests parse all generated C# with Roslyn
  (`Microsoft.CodeAnalysis.CSharp`, test project only), since the WinForms libraries are not
  available off Windows.

## Slice 13 — Editor improvements

- **Selection is a list.** The last control clicked is the most recent. With exactly one
  control selected the Properties panel edits it and it gets resize handles and anchor lines;
  with several, each gets an outline and the panel says how many are selected; with none,
  the panel edits the screen.
- **Mouse.** Ctrl+click toggles a control in the selection; Shift+click adds it. Dragging
  across blank canvas draws a selection box that selects every control it touches (with
  Ctrl or Shift, adding to the selection). A plain click on an already-selected control keeps
  the group so it can be dragged, and selects just that control if released without
  dragging.
- **Group move.** The control under the pointer snaps to the grid and the rest move by the
  same offset. The offset stops where any selected control would leave the screen, so the
  group keeps its shape. One drag is one undo step.
- **Keyboard.** Arrow keys nudge the selection by 1 DIP, or one grid step with Shift, one undo
  step per key press. Ctrl+A selects everything.
- **Copy and paste.** Copy, Cut, Paste and Duplicate (Ctrl+D) work within the editor, and
  across projects in the same editor window. Copies get new IDs; a name already in use gets
  the lowest free number (Button1 → Button2, SubmitButton → SubmitButton2). Each paste of the
  same copy lands one more grid step down and right, kept inside the screen. The system
  clipboard is not used yet.
- **Z-order.** Bring to Front (Ctrl+]) and Send to Back (Ctrl+[) keep the selected controls'
  order among themselves.
- **Screen size.** With nothing selected, the Properties panel sets the screen's width and
  height (100 to 10000 DIPs). A size that would leave a control outside is refused with its
  name. This changes only existing fields, so the file format is unchanged.

## Slice 14 — Containers: model and rules

- **Two containers.** `StackPanel` lines children up vertically or horizontally with a gap;
  `Grid` divides itself into equal rows and columns. Both are placed on the screen like any
  control (position, size, anchors) and can be nested.
- **The container owns placement.** A child's position comes from its container. Along a
  stack's direction the child keeps its own size and it stretches across; in a grid it fills
  its cell. This is what makes groups of controls resize together.
- **Equal grid cells for now.** Rows and columns are counts, not per-row sizes. Cell edges
  are rounded from exact fractions so cells tile with no gaps. Spans and sized rows can
  come later without breaking files: they would be new optional fields.
- **Reference rules in Core.** `ContainerLayout` arranges children and flattens the tree to
  screen positions at any window size; `ControlTree` does tree edits. Everything else (the
  designer, Preview, both generators, the tests) follows these rules.
- **Format version 3.** New types and fields, documented with the version history. Older
  files load unchanged.
- **Editing operations.** Add into a container at a drop point (a stack position or a grid
  cell), move into or out of a container, reorder inside one, set a child's size along its
  stack or its grid cell, and edit orientation, spacing, rows and columns. Each is one undo
  step. A container cannot be put inside itself.

## Slice 15 — Containers in the editor

- **One host per control, drawn from the Core layout.** In Design mode every control,
  including those inside containers, is drawn at the screen position Core computes, with
  containers drawn before what they hold. The innermost control under the pointer is the one
  clicked, and no separate layout code exists in the designer.
- **Design-mode look.** Containers show as a tinted, outlined area with their name, and grid
  cell lines. In Preview they are real WPF StackPanel and Grid controls holding the real
  controls.
- **Dragging.** Controls on the screen drag as before, snapped and together with the rest of
  the selection; a container brings everything inside it. A single control dragged over a
  container is dropped into it (the container is outlined in green), at the stack position or
  grid cell under the pointer. A control dragged out of its container and not over another
  is placed on the screen at its dragged position, snapped, with its own size.
- **Placing.** Dropping or click-placing a toolbox item inside a container puts it there.
- **Properties.** For a control inside a container, X, Y and Anchor are hidden. A stack shows
  only the size along its direction plus Earlier/Later order buttons. A grid shows the cell's
  row and column. Containers themselves show direction and spacing (StackPanel) or rows and
  columns (Grid).
- **Copying** a control from inside a container makes a free-standing copy at its screen
  position and size.
- **Resize handles and anchor lines** appear only for controls directly on the screen.

## Slice 16 — Containers in generated code

- **WPF** uses its own `StackPanel` and `Grid`, which follow the same rules natively: fixed
  size along a stack, stretch across it, spacing as a leading margin, equal `*` cells.
- **WinForms** has no stack with stretch-across and spacing, and its `FlowLayoutPanel`
  does not stretch children, so both containers become a `TableLayoutPanel` configured to
  match: fixed rows (or columns) sized child-plus-gap and a filler for stacks; equal percentage
  rows and columns for grids; children docked to fill.
- **A second sample,** `docs/samples/layout-demo.uibproj`, covers every container feature: a
  vertical stack of fields, a 3 × 2 grid of buttons with a stack nested in one cell, and a
  horizontal footer stack, all anchored so the window resizes. CI exports both samples to
  both targets, builds and runs all four apps.
- **Proof on Windows.** The parity test now walks the whole tree: in the generated WPF
  window and in the editor's Preview, every control (at any depth) must land where the Core
  rules say, at the design size and a larger size. The WinForms test does the same with the
  real running form, read through UI Automation. Both allow one pixel for how each framework
  rounds uneven grid cells.
- **Design-mode clipping.** Children that overflow a container are clipped to it in Design
  mode, as they are at run time, except while a child is being dragged out.

## Slice 17 — Grid row and column spans

- **Spans** let a control inside a Grid cover several rows and columns from its cell. They are
  stored as optional `rowSpan` and `columnSpan` and omitted when 1.
- **Format version 4.** An older builder reading a file with spans would ignore them and drop
  them on save, so the version was bumped: older builders now refuse the file instead.
- **Spans always fit.** A span that would reach past the grid is refused, as are moving a
  spanned control to a cell where it would not fit and shrinking a grid below what its
  children span. Dragging a control to another cell of the same grid keeps its span when it
  still fits; moving it into a different container or onto the screen drops the span.
- **Overlap is allowed,** as for controls on the screen: a spanning control can cover cells
  that hold other controls. Later controls are drawn on top.
- **Output.** WPF writes `Grid.RowSpan`/`Grid.ColumnSpan`; WinForms calls `SetRowSpan` and
  `SetColumnSpan` on the `TableLayoutPanel`. The layout-demo sample now has a button spanning
  the grid's top row, so the Windows layout tests cover spans in both targets.

## Slice 18 — Sized grid rows and columns

- **Two kinds of size,** in WPF's notation: fixed DIPs (`"100"`) and a weighted share of the
  space left (`"*"`, `"2*"`). Fixed sizes are taken first and shares split the rest. If fixed
  sizes overflow, shares get nothing and the overflow is clipped, as in WPF.
- **No `Auto`.** Sizing a row to its content needs every target to measure controls the same
  way, which the builder cannot promise, so it is left out rather than approximated.
- **Stored only when needed,** as `rowSizes` and `columnSizes` lists, one entry per row or
  column, written canonically (`"2*"`, not `" 2 *"`). When every size is `"*"` they are
  omitted. Changing the row or column count keeps existing sizes and adds equal shares.
- **Format version 5,** for the same reason as spans: an older builder would silently drop
  the sizes.
- **One set of rules.** `GridTrackSize.Edges` in Core computes the boundaries. The designer's
  cell lines, drop targets, Preview, WPF output (`RowDefinition Height`) and WinForms output
  (`Absolute` rows plus percentages of the remaining space) all follow it. The layout demo
  now has a fixed 60-DIP top row and columns sharing 2:1, so the Windows layout checks cover
  sized tracks in both targets.

## Slice 19 — Multiple screens

- **A list of screens.** A project holds `screens` in order, always at least one. Control
  names only need to be unique within their screen, since each screen becomes its own class;
  screen names are identifiers, unique across the project. Control IDs stay unique across
  the whole project, so copying between screens never produces duplicates.
- **Which screen is showing is editor state,** like the selection: not saved, not an edit,
  and switching screens never marks the project changed. Each undo step remembers the screen
  shown before and after the change, so undo and redo bring back the screen where the
  change is visible.
- **Tabs above the canvas** rather than a separate panel: they take one row, show every
  screen at once and read naturally left to right as the screen order. The Screen menu holds
  the commands, and the Properties panel's Screen section (shown when nothing is selected)
  gains the name. Ctrl+PageUp and Ctrl+PageDown switch screens, handled before the canvas's
  scroll viewer, which would otherwise take them as page scrolling.
- **The first screen is the main window.** In output it is always `MainWindow`/`MainForm`,
  whatever its name, so `App.xaml` and `Program.cs` (created once and then the developer's)
  never need to change. Other screens are named after themselves (`SettingsWindow`,
  `SettingsForm`). The only clash this allows, a later screen named `Main`, blocks export
  with a message.
- **No navigation yet.** Opening one screen from another is an action, which the spec defers;
  the generated code comments show the one line that does it from a hook. The Windows
  WinForms test implements exactly that hook to open the layout demo's second screen and
  check its layout.
- **Format version 6.** Older files' single `screen` becomes the only entry in `screens`.
  Files are never deleted on export: a renamed or removed screen's old files stay, with any
  code the developer wrote in them.


## Slice 20 — More controls

- **The batch:** RadioButton, ListBox, Slider, ProgressBar, DatePicker, PasswordBox, a
  multi-line option on TextBox, and the GroupBox container. Each exists in both WPF and
  WinForms and needs no new concept. TabControl, images, menus and data grids are left for
  later: they need tabs within a screen, stored images, or data binding.
- **Radio groups follow the container.** Both frameworks group radio buttons by their parent
  when no group name is given, so the builder does the same rather than adding a group-name
  property WinForms lacks. Choosing one in the editor clears the others in that container in
  one undo step. In Preview, controls on the screen each sit in their own host, so their
  radio buttons share a group name there to behave like the generated window.
- **GroupBox is a titled stack.** A frame whose children are placed at absolute positions
  would be a new layout model; lining them up like a StackPanel reuses the existing rules.
  Children sit inside a fixed inset (8, 20, 8, 8), not the theme's content area, which
  differs between WPF, WinForms and DPI settings. Both generators draw the real GroupBox and
  lay the children out in a panel at that inset, so positions match exactly.
- **Only what is portable is stored.** Slider and ProgressBar keep whole-number minimum,
  maximum and value. A DatePicker has no stored date (WPF shows an empty picker; WinForms an
  unticked one), and a PasswordBox never stores a password. Multi-line is stored only when
  on, so existing files and single-line text boxes are unchanged.
- **Format version 7,** since an older builder would reject the new types (clearly) but
  silently drop `isMultiline`.
- **WinForms sizes.** `ListBox.IntegralHeight` and `TrackBar.AutoSize` are turned off so the
  designed height holds; single-line text boxes and date pickers still take their height
  from the font, as ComboBox already did.
- The layout demo's Settings screen now uses every new control, so the Windows layout
  checks cover them in Preview, WPF and WinForms.

## Slice 21 — Buttons that open and close screens

- **The smallest useful action.** A Button can open another screen or close its own. That
  makes multi-screen designs work end to end without code, while anything richer (passing
  values, conditions) stays in the hooks, where C# already does it well. The spec defers
  actions in general; this is deliberately the only one.
- **Opened as a dialog.** Dialogs are the common case for a second screen in a desktop tool,
  need no window management, and behave the same in WPF (`ShowDialog` with an owner) and
  WinForms (`ShowDialog(this)`).
- **Screens are linked by ID,** so renaming a screen keeps its buttons working and the
  generated code follows the new class name. Deleting a screen clears the links to it in
  the same undo step; a file that links to a missing screen is rejected on load.
- **The hook runs first,** then the action, from the regenerated handler, so developer code
  can prepare the next screen (or save) before it opens or the current one closes.
- **Preview follows actions.** Clicking a button switches the preview to the screen it opens,
  and a closing button goes back to the screen that opened it. Switching waits until the
  click has finished, since it rebuilds the Preview, including the button.
- **Format version 8.**

## Slice 22 — Blazor output

- **Why Blazor next.** Of the targets the spec names, it is the one that reaches beyond
  Windows desktops, and the one that can be built, run and checked everywhere, including on
  the Linux machine the builder is developed on. WinUI 3 and MAUI need platform workloads.
- **Interactive server rendering,** from the standard Blazor Web App template, trimmed: one
  project, `dotnet run`, no JavaScript of its own. WebAssembly would need a second project.
- **Plain CSS for layout,** following the Core rules one to one: absolute positions from
  anchors on the screen, flex boxes for stacks, CSS grid (with `px` and `fr` tracks) for
  grids, and border-box sizing so every box is the designed size. No layout library.
- **Values are fields.** Where WPF and WinForms code reads a control (`NameTextBox.Text`),
  Blazor code binds controls to page fields. Each value control gets a field named after it,
  initialised from the design, and hooks take no arguments. Hook names match the WPF output.
- **Radio groups** use the container as the HTML `name`, and the generated handler clears
  the rest of the group, since HTML radios bound to separate fields do not do it themselves.
- **Closing a screen goes back.** A web page cannot close itself; `history.back()` returns to
  the page that opened it, which is what closing a dialog does in the desktop output.
- **Checked in a real browser.** A new opt-in test project builds and runs each exported
  sample and compares every control's box, read with `getBoundingClientRect`, with the Core
  layout at the design size and larger (within a pixel, for fractional grid tracks). It uses
  Microsoft.Playwright to drive an installed Edge on Windows or a given Chromium elsewhere, so
  nothing is downloaded. Playwright's own box query briefly returned nothing while Blazor
  re-rendered the page on connecting, so boxes are read directly from the page instead.

## Slice 23 — Text size, bold and colours

- **The smallest useful styling.** The spec defers themes; four per-control settings (text
  size, bold, text colour, background) cover most of what a form needs to look deliberate,
  and every target has a direct equivalent. Font families, italics, borders and shared
  styles are left for a real theming design.
- **Sizes in DIPs, colours as `#RRGGBB`.** DIPs match the rest of the design and WPF and CSS
  pixels; WinForms gets points (3/4 of a DIP). `#RRGGBB` is read by WPF and CSS as is, and
  WinForms builds it with `Color.FromArgb`. Input accepts either case and an optional `#`;
  files store the upper-case form, and anything else is rejected on load.
- **Only text controls have a font and text colour;** any control can have a background.
  Unset values are not stored, so existing files and output are unchanged.
- **A GroupBox's font and text colour style its title only,** as WPF does, since its children
  are not inside the WPF GroupBox. WinForms and CSS pass fonts down to children, so the
  WinForms layout panel resets them and the Blazor output styles the title element instead.
- **Format version 9.** The WPF parity test now also compares font and colours between the
  designer and the generated window, and the browser test reads computed styles.
