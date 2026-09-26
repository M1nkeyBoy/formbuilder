using StandaloneUiBuilder.Libraries;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>The sample control library (tests/StandaloneUiBuilder.SampleControls), scanned as a library package.</summary>
internal static class SampleLibrary
{
    public const string PackageId = "Sample.Controls";

    public static string AssemblyPath => Path.Combine(AppContext.BaseDirectory, "SampleControls", "StandaloneUiBuilder.SampleControls.dll");

    public static IReadOnlyList<LibraryControl> Scan(ProjectPlatform platform) => ControlScanner.Scan(platform, [AssemblyPath], [AssemblyPath]);

    public static LibraryPackage Package(ProjectPlatform platform, string version = "1.2.0") =>
        new() { Id = PackageId, Version = version, Controls = [.. Scan(platform)] };

    /// <summary>A project for the platform with the sample library, and an editor on it.</summary>
    public static DesignEditor Editor(ProjectPlatform platform)
    {
        var editor = new DesignEditor();
        editor.New(platform);
        Assert.Null(editor.AddLibrary(Package(platform)));
        return editor;
    }
}
