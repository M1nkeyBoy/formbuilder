# Standalone UI Builder

A Windows desktop designer for placing and editing controls on a gridded canvas.
It is being built in review-gated slices; see `docs/decisions.md` for choices made so far.

**Current state (Slice 3):** drag Label, Button, TextBox, CheckBox or ComboBox from the
toolbox onto the 800 × 600 gridded canvas (or select a toolbox item and click the canvas or
press Enter). Select a control to move it by dragging, resize it with its handles, or edit
its name, position, size and type-specific values in the Properties panel. Delete removes
it; Ctrl+Z and Ctrl+Y undo and redo. Saving comes in the next slice.

## Requirements

- Windows 10 or 11 to run the editor.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100 or a later 10.0 feature band; see `global.json`).

Visual Studio is optional. It is not needed to build or run the editor.

## Build and run

From the repository root:

```powershell
dotnet build StandaloneUiBuilder.sln
dotnet run --project src/StandaloneUiBuilder
```

You can also launch the built executable directly:

```powershell
src\StandaloneUiBuilder\bin\Debug\net10.0-windows\StandaloneUiBuilder.exe
```

## Tests

```powershell
dotnet test StandaloneUiBuilder.sln
```

The Core library and its tests target plain `net10.0`, so they also build and run on Linux and macOS.

## Repository layout

```text
StandaloneUiBuilder.sln
src/StandaloneUiBuilder/              WPF shell, canvas and rendering
src/StandaloneUiBuilder.Core/         Document model, commands, validation, serialization
tests/StandaloneUiBuilder.Core.Tests/ Tests for the Core library
docs/                                 Decisions and format notes
```

## Continuous integration

`.github/workflows/windows-build.yml` builds and tests on a Windows runner, launches the
editor, and uploads a screenshot of the running window as the `screenshot` artifact.
The same image is printed to the job log as base64 between `SCREENSHOT-BASE64-BEGIN`
and `SCREENSHOT-BASE64-END`.
