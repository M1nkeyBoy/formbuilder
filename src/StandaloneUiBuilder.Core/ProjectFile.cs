using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StandaloneUiBuilder.Core;

/// <summary>A project file could not be read or written. The message is suitable for the user.</summary>
public sealed class ProjectFileException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Reads and writes <c>.uibproj</c> files: versioned JSON described in docs/project-format.md.
/// </summary>
public static partial class ProjectFile
{
    public const string Extension = ".uibproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new AnchorEdgesJsonConverter(), new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string Serialize(ProjectDocument document) =>
        JsonSerializer.Serialize(document with { SchemaVersion = ProjectDocument.CurrentSchemaVersion }, Options);

    /// <summary>
    /// Parses and validates project JSON. Throws <see cref="ProjectFileException"/> with a
    /// plain-language reason if it is not a usable project.
    /// </summary>
    public static ProjectDocument Deserialize(string json)
    {
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ProjectFileException("The file is not valid JSON.", ex);
        }

        using (parsed)
        {
            if (CheckSchemaVersion(parsed.RootElement) < 6)
            {
                json = WrapSingleScreen(json);
            }

            using var migrated = JsonDocument.Parse(json);
            CheckControlTypes(migrated.RootElement);
        }

        ProjectDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProjectDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            var where = string.IsNullOrEmpty(ex.Path) ? "" : $" (at {ex.Path})";
            throw new ProjectFileException($"The file is not a valid project{where}.", ex);
        }

        if (document?.Screens is not { Count: > 0 } || document.Screens.Contains(null!))
        {
            throw new ProjectFileException("The file is not a valid project: it has no screen.");
        }

        // Fill in type-specific values that older or hand-edited files omit, and drop values
        // the control type does not use.
        document = document with
        {
            Screens = document.Screens.ConvertAll(screen => screen with { Controls = screen.Controls.ConvertAll(Normalize) }),
        };

        var errors = DocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new ProjectFileException("The project contains invalid data:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(e => "• " + e)));
        }

        return document;
    }

    /// <summary>
    /// Fills in type-specific values that older or hand-edited files omit, drops values the
    /// type does not use, and gives containers an empty child list, all the way down the tree.
    /// </summary>
    private static ControlDocument Normalize(ControlDocument control)
    {
        if (!ControlCatalog.TryGet(control.Type, out var definition))
        {
            return control;
        }

        return control with
        {
            Properties = definition.Normalize(control.Properties ?? new ControlProperties()),
            Children = definition.IsContainer ? (control.Children ?? []).ConvertAll(Normalize)
                : control.Children is { Count: 0 } ? null
                : control.Children,
        };
    }

    /// <summary>
    /// Writes the project to a temporary file beside the destination and then moves it into
    /// place, so an interrupted save never leaves a truncated project file.
    /// </summary>
    public static void Save(ProjectDocument document, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Serialize(document));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(tempPath);
            throw new ProjectFileException(ex.Message, ex);
        }
    }

    public static ProjectDocument Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new ProjectFileException(ex.Message, ex);
        }

        return Deserialize(json);
    }

    /// <summary>
    /// Versions 1 to 5 hold one "screen"; version 6 holds a list of "screens". An older file's
    /// screen becomes the only one. Those versions had no way to rename the screen, so a name
    /// that is not an identifier (only possible by hand-editing) becomes "Main".
    /// </summary>
    private static string WrapSingleScreen(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root || root["screen"] is not JsonObject screen || root.ContainsKey("screens"))
        {
            return json;
        }

        if (screen["name"] is not JsonValue name || !name.TryGetValue<string>(out var text) || !IdentifierPattern().IsMatch(text))
        {
            screen["name"] = ScreenDocument.DefaultName;
        }

        root.Remove("screen");
        root["screens"] = new JsonArray(screen);
        return root.ToJsonString();
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    private static int CheckSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var versionElement)
            || !versionElement.TryGetInt32(out var version))
        {
            throw new ProjectFileException("The file is not a UI Builder project: it has no schema version.");
        }

        if (version > ProjectDocument.CurrentSchemaVersion)
        {
            throw new ProjectFileException(
                $"The project was saved by a newer version of the builder (format version {version}). "
                + $"This version supports format version {ProjectDocument.CurrentSchemaVersion}.");
        }

        if (version < ProjectDocument.OldestSupportedSchemaVersion)
        {
            throw new ProjectFileException($"The project format version {version} is not valid.");
        }

        // Version 1 had no anchors; every control loads with the default (left and top), which
        // is how version 1 designs behaved. Versions 3 to 5 added containers, grid spans and
        // sized grid rows and columns; older files have none of them, so need nothing else.
        // Version 6 allows several screens; older files are read as having one. Version 7 adds
        // control types and properties that older files do not use, version 8 button actions,
        // version 9 fonts and colours, version 10 images, version 11 tab controls, and version 12 tab order.
        return version;
    }

    // Unknown types would otherwise fail inside the JSON reader with an unhelpful message.
    private static void CheckControlTypes(JsonElement root)
    {
        if (!root.TryGetProperty("screens", out var screens) || screens.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var screen in screens.EnumerateArray())
        {
            if (screen.ValueKind == JsonValueKind.Object && screen.TryGetProperty("controls", out var controls))
            {
                CheckControlList(controls);
            }
        }
    }

    private static void CheckControlList(JsonElement controls)
    {
        if (controls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var control in controls.EnumerateArray())
        {
            if (control.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (control.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                && !(Enum.TryParse<ControlType>(typeElement.GetString(), ignoreCase: true, out var type) && Enum.IsDefined(type)))
            {
                var name = control.TryGetProperty("name", out var nameElement) ? nameElement.ToString() : "(unnamed)";
                throw new ProjectFileException(
                    $"The project uses a control type this version does not support: \"{typeElement.GetString()}\" (control \"{name}\").");
            }

            if (control.TryGetProperty("children", out var children))
            {
                CheckControlList(children);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
