using System.Text.Json;
using System.Text.Json.Nodes;

namespace StandaloneUiBuilder.Core;

/// <summary>A draft left behind by an editing session that did not close normally.</summary>
public sealed record RecoveryDraft(string DraftPath, string? ProjectPath, DateTimeOffset SavedAt, ProjectDocument Document);

/// <summary>
/// Keeps recovery drafts of unsaved work in a local folder, separate from project files.
/// Each running session holds a lock file; a draft whose lock is not held belongs to a
/// session that ended without cleaning up, and can be offered for recovery.
/// </summary>
public sealed class RecoveryStore(string directory)
{
    private const string DraftSuffix = ".draft.json";
    private const string LockSuffix = ".lock";
    private const int RecoveryFormatVersion = 1;

    public string Directory { get; } = directory;

    public RecoverySession StartSession()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var id = Guid.NewGuid().ToString("N");
        var lockStream = new FileStream(
            Path.Combine(Directory, id + LockSuffix),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.DeleteOnClose);
        return new RecoverySession(Path.Combine(Directory, id + DraftSuffix), lockStream);
    }

    /// <summary>
    /// Returns drafts from sessions that are no longer running, newest first. Drafts that
    /// cannot be read are deleted, since they cannot be recovered.
    /// </summary>
    public IReadOnlyList<RecoveryDraft> FindOrphanedDrafts()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var drafts = new List<RecoveryDraft>();
        foreach (var draftPath in System.IO.Directory.EnumerateFiles(Directory, "*" + DraftSuffix))
        {
            var id = Path.GetFileName(draftPath)[..^DraftSuffix.Length];
            if (IsSessionRunning(Path.Combine(Directory, id + LockSuffix)))
            {
                continue;
            }

            if (TryReadDraft(draftPath) is { } draft)
            {
                drafts.Add(draft);
            }
            else
            {
                Discard(draftPath);
            }
        }

        return [.. drafts.OrderByDescending(d => d.SavedAt)];
    }

    public static void Discard(string draftPath)
    {
        try
        {
            File.Delete(draftPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static string SerializeDraft(ProjectDocument document, string? projectPath, DateTimeOffset savedAt) =>
        new JsonObject
        {
            ["recoveryVersion"] = RecoveryFormatVersion,
            ["savedAt"] = savedAt,
            ["projectPath"] = projectPath,
            ["project"] = JsonNode.Parse(ProjectFile.Serialize(document)),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static RecoveryDraft? TryReadDraft(string draftPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(draftPath))!.AsObject();
            if (root["recoveryVersion"]?.GetValue<int>() != RecoveryFormatVersion || root["project"] is not { } project)
            {
                return null;
            }

            return new RecoveryDraft(
                draftPath,
                root["projectPath"]?.GetValue<string>(),
                root["savedAt"]!.GetValue<DateTimeOffset>(),
                ProjectFile.Deserialize(project.ToJsonString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or FormatException or NullReferenceException or ProjectFileException)
        {
            return null;
        }
    }

    private static bool IsSessionRunning(string lockPath)
    {
        if (!File.Exists(lockPath))
        {
            return false;
        }

        try
        {
            // Opening succeeds only if no running session holds the lock.
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }

        // A leftover lock file nobody holds; tidy it up.
        try
        {
            File.Delete(lockPath);
        }
        catch (IOException)
        {
        }

        return false;
    }
}

/// <summary>The recovery draft slot of one running editor session.</summary>
public sealed class RecoverySession(string draftPath, FileStream lockStream) : IDisposable
{
    public string DraftPath { get; } = draftPath;

    /// <summary>Writes (or replaces) this session's draft. The write is atomic.</summary>
    public void WriteDraft(ProjectDocument document, string? projectPath)
    {
        var tempPath = DraftPath + ".tmp";
        File.WriteAllText(tempPath, RecoveryStore.SerializeDraft(document, projectPath, DateTimeOffset.Now));
        File.Move(tempPath, DraftPath, overwrite: true);
    }

    public void DeleteDraft() => RecoveryStore.Discard(DraftPath);

    /// <summary>Releases the session lock. The draft is left in place unless deleted first.</summary>
    public void Dispose() => lockStream.Dispose();
}
