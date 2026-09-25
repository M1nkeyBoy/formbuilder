# Standalone UI Builder

A Windows desktop designer for placing and editing controls on a gridded canvas, saving the
design as a project file and reopening it later. This is the first prototype; it does not
generate application code. Design choices are recorded in `docs/decisions.md`.

## What it does

- **Place controls.** Drag Label, Button, TextBox, CheckBox or ComboBox from the toolbox onto
  the 800 × 600 canvas. Or select a toolbox item and then click the canvas, or press Enter.
  Placement snaps to the 10-DIP grid.
- **Edit.** Click a control to select it. Drag it to move it, or drag its handles to resize
  it. Moves and resizes snap to the grid and stay inside the canvas. Delete removes the
  selected control.
- **Properties.** The Properties panel edits name, X, Y, width and height for every control,
  plus text, checked state or ComboBox items (one per line) where they apply. Press Enter or
  leave a field to apply it; Esc restores the stored value. Invalid values are flagged in red
  and never written to the design.
- **Undo and redo.** Ctrl+Z and Ctrl+Y cover adding, deleting, moving, resizing and property
  changes. A whole drag is one step.
- **Preview.** Switch to Preview (bottom left, or View > Preview) to try the controls. Type
  in text boxes, tick check boxes, pick ComboBox items and click buttons. Nothing you do in
  Preview changes the design.
- **Save and open.** File > New, Open, Save (Ctrl+S) and Save As (Ctrl+Shift+S) work with
  `.uibproj` files. The title shows ● when there are unsaved changes, and you are asked to
  Save, Discard or Cancel before they could be lost.
- **Recovery.** If the editor closes unexpectedly, it offers to recover your unsaved work
  the next time it starts.

Try it with the sample: File > Open > `docs\samples\customer-form.uibproj`.

| Design | Preview |
|---|---|
| ![Design mode with a selected button and the Properties panel](docs/screenshots/design-mode.png) | ![Preview mode with typed text, a ticked check box and a chosen ComboBox item](docs/screenshots/preview-mode.png) |

More in [`docs/screenshots`](docs/screenshots): the sample project, and a draft recovered
after a forced close. All were captured on Windows by the CI walkthrough.

## Open in Visual Studio

1. Install Visual Studio 2026 (or 2022 17.14 or later) with the **.NET desktop development**
   workload, and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) if
   Visual Studio did not install it.
2. Open `StandaloneUiBuilder.sln`.
3. Make sure **StandaloneUiBuilder** is the startup project (right-click it > Set as Startup
   Project), then press F5.

To open the sample at startup while debugging, set it as the command-line argument in the
project's debug properties.

## Build and run from the command line

Requires Windows 10 or 11 and the .NET 10 SDK (10.0.100 or a later 10.0 feature band; see
`global.json`). Visual Studio is not needed.

```powershell
dotnet build StandaloneUiBuilder.sln
dotnet run --project src/StandaloneUiBuilder
dotnet run --project src/StandaloneUiBuilder -- docs\samples\customer-form.uibproj
```

## Release build

A self-contained, single-file build runs without Visual Studio or an installed .NET runtime:

```powershell
dotnet publish src/StandaloneUiBuilder/StandaloneUiBuilder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
publish\StandaloneUiBuilder.exe
```

CI builds the same thing on every push. Download it from the run's
`StandaloneUiBuilder-win-x64` artifact on the repository's Actions page.

## Tests

```powershell
dotnet test StandaloneUiBuilder.sln
```

- `tests/StandaloneUiBuilder.Core.Tests` covers the document model, editing rules, undo,
  saving and loading, and recovery. It also runs on Linux and macOS.
- `tests/StandaloneUiBuilder.UiTests` is an end-to-end walkthrough of the prototype
  acceptance steps, plus checks that editing stays responsive with 60 controls and that
  Tab reaches the toolbox and Properties panel. It drives the real editor with the mouse and keyboard, so it is skipped
  unless you opt in:

  ```powershell
  $env:UIB_RUN_UI_TESTS = "1"
  dotnet test tests/StandaloneUiBuilder.UiTests
  ```

  Leave the mouse and keyboard alone while it runs. It assumes 100% display scaling.
  Screenshots go to `artifacts/ui-walkthrough`.

## Recovery copies

While a project has unsaved changes, a recovery copy is written two seconds after the last
edit to `%LOCALAPPDATA%\StandaloneUiBuilder\Recovery`. It is deleted when you save, discard
or close normally. Recovery copies never overwrite your project file. Setting the
`UIB_RECOVERY_DIR` environment variable stores them in a different folder instead; the UI
tests use this so they never touch your own drafts.

## Known limitations

- One screen per project, with a fixed 800 × 600 size; there is no UI to change it yet.
- Only the five built-in controls. No containers, layout panels, z-order commands, multiple
  selection, copy/paste or keyboard nudging.
- Positions are absolute. This suits the prototype but not the responsive layouts that later
  WinUI, MAUI or Blazor output will need (see the spec's "decisions to revisit").
- No code generation or import of existing projects.
- The automated UI tests assume 100% display scaling. 150% scaling has been checked by hand.
- Only the most recent recovery draft is offered at each start; older ones wait for later
  starts.

## Repository layout

```text
StandaloneUiBuilder.sln
src/StandaloneUiBuilder/              WPF editor: window, design surface, preview
src/StandaloneUiBuilder.Core/         Document model, editing rules, undo, file format, recovery
tests/StandaloneUiBuilder.Core.Tests/ Unit tests for Core
tests/StandaloneUiBuilder.UiTests/    End-to-end UI walkthrough (Windows, opt-in)
docs/                                 Decisions, project file format and samples
```

## Continuous integration

`.github/workflows/windows-build.yml` runs on a Windows runner on every push:

1. Builds the solution and runs the Core tests.
2. Runs the UI walkthrough.
3. Opens the sample project and takes a screenshot.
4. Uploads every screenshot as the `screenshots` artifact, and also prints each one to the
   job log as base64 between `SCREENSHOT-BASE64-BEGIN <name>` and `SCREENSHOT-BASE64-END`.
5. Publishes the self-contained build, checks that it starts, and uploads it.
