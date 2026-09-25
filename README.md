# Standalone UI Builder

A Windows desktop designer for placing and editing controls on a gridded canvas.
It is being built in review-gated slices; see `docs/decisions.md` for choices made so far.

**Current state (Slice 0):** a launchable shell with Toolbox, Design surface and
Properties regions, menus and a Design/Preview toggle. None of the editing features work yet.

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
