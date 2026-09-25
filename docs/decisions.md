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
