using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Preview;

/// <summary>
/// What the editor asks the preview host to draw: a library control, by type, with the values
/// the design sets, at the design's size. One JSON line on the host's standard input.
/// </summary>
internal sealed record PreviewRequest(
    int Id,
    ProjectPlatform Platform,
    string TypeName,
    string Assembly,
    IReadOnlyList<LibrarySetting> Settings,
    int Width,
    int Height,
    bool Dark,
    IReadOnlyList<string> Assemblies,
    IReadOnlyDictionary<string, string> LicenseKeys);

/// <summary>The host's answer, one JSON line on its standard output: a PNG as base64, or why there is none.</summary>
internal sealed record PreviewResponse(int Id, string? Png, string? Error);
