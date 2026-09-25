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
