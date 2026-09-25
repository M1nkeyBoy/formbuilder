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
