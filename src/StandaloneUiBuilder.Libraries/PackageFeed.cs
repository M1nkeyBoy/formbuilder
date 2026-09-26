using System.Net;
using System.Text.Json;

namespace StandaloneUiBuilder.Libraries;

/// <summary>A library could not be added. The message is suitable for the user.</summary>
public sealed class LibraryException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Where packages come from: nuget.org, or a folder of .nupkg files.</summary>
public interface IPackageFeed
{
    /// <summary>The package's versions, oldest first; empty if the feed does not have it.</summary>
    Task<IReadOnlyList<string>> GetVersionsAsync(string id, CancellationToken cancellationToken);

    /// <summary>The .nupkg file's content.</summary>
    Task<Stream> OpenPackageAsync(string id, string version, CancellationToken cancellationToken);
}

/// <summary>nuget.org, or another feed with the NuGet v3 flat container API.</summary>
public sealed class FlatContainerFeed(HttpClient http, string baseUrl = FlatContainerFeed.NuGetOrg) : IPackageFeed
{
    public const string NuGetOrg = "https://api.nuget.org/v3-flatcontainer/";

    public async Task<IReadOnlyList<string>> GetVersionsAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await Send($"{baseUrl}{id.ToLowerInvariant()}/index.json", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return json.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToList();
    }

    public async Task<Stream> OpenPackageAsync(string id, string version, CancellationToken cancellationToken)
    {
        var lower = id.ToLowerInvariant();
        var v = version.ToLowerInvariant();
        var response = await Send($"{baseUrl}{lower}/{v}/{lower}.{v}.nupkg", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new LibraryException($"{id} {version} is not on the package feed.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> Send(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LibraryException($"Could not reach the package feed: {ex.Message}", ex);
        }
    }
}

/// <summary>A folder of .nupkg files named {id}.{version}.nupkg, as a local NuGet feed has.</summary>
public sealed class FolderFeed(string folder) : IPackageFeed
{
    public Task<IReadOnlyList<string>> GetVersionsAsync(string id, CancellationToken cancellationToken)
    {
        var prefix = id + ".";
        IReadOnlyList<string> versions = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.nupkg")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && char.IsDigit(name[prefix.Length]))
                .Select(name => name![prefix.Length..])
                .OrderBy(v => v, PackageVersion.Comparer)
                .ToList()
            : [];
        return Task.FromResult(versions);
    }

    public Task<Stream> OpenPackageAsync(string id, string version, CancellationToken cancellationToken)
    {
        var path = Directory.GetFiles(folder, "*.nupkg")
            .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), $"{id}.{version}", StringComparison.OrdinalIgnoreCase))
            ?? throw new LibraryException($"{id} {version} is not in {folder}.");
        return Task.FromResult<Stream>(File.OpenRead(path));
    }
}

/// <summary>NuGet versions: compared by their numbers, with prereleases before releases.</summary>
public static class PackageVersion
{
    public static IComparer<string> Comparer { get; } = Comparer<string>.Create(Compare);

    public static bool IsPrerelease(string version) => version.Contains('-', StringComparison.Ordinal);

    public static int Compare(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return string.CompareOrdinal(a, b);
        }

        var (numbersA, labelA) = Split(a);
        var (numbersB, labelB) = Split(b);
        for (var i = 0; i < Math.Max(numbersA.Length, numbersB.Length); i++)
        {
            var x = i < numbersA.Length ? numbersA[i] : 0;
            var y = i < numbersB.Length ? numbersB[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        return (labelA, labelB) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => string.Compare(labelA, labelB, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static (long[] Numbers, string? Label) Split(string version)
    {
        version = version.Split('+')[0];
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        var label = dash < 0 ? null : version[(dash + 1)..];
        var numbers = (dash < 0 ? version : version[..dash]).Split('.')
            .Select(p => long.TryParse(p, out var n) ? n : 0).ToArray();
        return (numbers, label);
    }
}
