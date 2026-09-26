using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Libraries;

/// <summary>
/// Adds a control library: downloads the package and the packages it depends on, and finds
/// the controls it has for the project's platform.
/// </summary>
public static class LibraryLoader
{
    /// <summary>The most packages followed through dependencies, to keep a download bounded.</summary>
    public const int MaxPackages = 40;

    /// <summary>
    /// Packages the platform itself provides, or that never define controls: the scanner
    /// recognises the platform's classes by name, so these need not be downloaded.
    /// </summary>
    private static readonly string[] SkippedPrefixes =
    [
        "Microsoft.WindowsAppSDK", "Microsoft.Windows.SDK", "Microsoft.Windows.CsWinRT", "Microsoft.Maui.", "Microsoft.AspNetCore.",
        "Microsoft.NETCore.", "Microsoft.Extensions.", "Microsoft.JSInterop", "System.", "NETStandard.Library", "Microsoft.CSharp",
        "Microsoft.Win32.", "Microsoft.Web.WebView2", "Microsoft.VisualBasic", "Microsoft.Bcl.", "Microsoft.NET.", "runtime.",
    ];

    public static bool IsSkipped(string id) => SkippedPrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <param name="version">The version to use; null for the newest release.</param>
    public static async Task<LibraryPackage> LoadAsync(
        PackageCache cache,
        string id,
        string? version,
        ProjectPlatform platform,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        id = id.Trim();
        if (id.Length == 0)
        {
            throw new LibraryException("Type a package name, such as Syncfusion.SfGrid.WPF.");
        }

        if (platform == ProjectPlatform.Any)
        {
            throw new LibraryException("Choose the project's platform first: a library's controls exist on one platform only.");
        }

        progress?.Report($"Looking for {id}…");
        version = string.IsNullOrWhiteSpace(version) ? await cache.LatestVersionAsync(id, cancellationToken).ConfigureAwait(false) : version.Trim();
        if (version is null)
        {
            throw new LibraryException($"There is no package called {id}. Check the name on nuget.org.");
        }

        progress?.Report($"Downloading {id} {version}…");
        var folder = await cache.GetAsync(id, version, cancellationToken).ConfigureAwait(false);
        var nuspec = PackageCache.ReadNuspec(folder);
        var scan = PackageCache.Assemblies(folder, platform).ToList();

        // Everything the package brings, for following base classes into its dependencies.
        var references = new List<string>(scan);
        var bundle = scan.Count == 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { nuspec.Id };
        var queue = new Queue<(string Id, string Version, int Depth)>();
        Enqueue(nuspec, 1);
        while (queue.Count > 0 && seen.Count < MaxPackages)
        {
            var (dependencyId, dependencyVersion, depth) = queue.Dequeue();
            if (!seen.Add(dependencyId))
            {
                continue;
            }

            progress?.Report($"Downloading {dependencyId} {dependencyVersion}…");
            string dependencyFolder;
            try
            {
                dependencyFolder = await cache.GetAsync(dependencyId, dependencyVersion, cancellationToken).ConfigureAwait(false);
            }
            catch (LibraryException)
            {
                // A dependency the feed lacks only means fewer base classes to follow.
                continue;
            }

            var assemblies = PackageCache.Assemblies(dependencyFolder, platform);
            references.AddRange(assemblies);

            // A package with no assemblies of its own, such as a bundle, offers its direct dependencies' controls.
            if (bundle && depth == 1)
            {
                scan.AddRange(assemblies);
            }

            Enqueue(PackageCache.ReadNuspec(dependencyFolder), depth + 1);
        }

        if (scan.Count == 0)
        {
            throw new LibraryException($"{nuspec.Id} {version} has nothing for {platform.DisplayName()}. Check that it is a {platform.DisplayName()} package.");
        }

        progress?.Report($"Finding the controls in {nuspec.Id}…");
        var controls = ControlScanner.Scan(platform, scan, references);
        if (controls.Count == 0)
        {
            throw new LibraryException($"{nuspec.Id} {version} has no {platform.DisplayName()} controls the builder can place.");
        }

        return new LibraryPackage { Id = nuspec.Id.Length > 0 ? nuspec.Id : id, Version = version, Controls = [.. controls] };

        void Enqueue(Nuspec spec, int depth)
        {
            var group = TargetFramework.Best(spec.Groups.Where(g => g.Framework is not null).Select(g => g.Framework!), platform) is { } best
                ? spec.Groups.First(g => g.Framework == best)
                : spec.Groups.FirstOrDefault(g => g.Framework is null);
            foreach (var dependency in group?.Dependencies ?? [])
            {
                if (!IsSkipped(dependency.Id) && dependency.Version is { } v && !seen.Contains(dependency.Id))
                {
                    queue.Enqueue((dependency.Id, v, depth));
                }
            }
        }
    }
}
