using System.IO.Compression;

namespace StandaloneUiBuilder.Libraries;

/// <summary>
/// Packages downloaded once and kept, unpacked, in a folder of the builder's own. Packages the
/// machine already has in NuGet's global packages folder are used from there.
/// </summary>
public sealed class PackageCache(IPackageFeed feed, string folder)
{
    /// <summary>The builder's default cache, in the user's local application data.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StandaloneUiBuilder", "Packages");

    /// <summary>NuGet's global packages folder, where builds put the packages they restore.</summary>
    public static string GlobalPackagesFolder =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    public IPackageFeed Feed => feed;

    /// <summary>The newest release of a package, or its newest version if it only has prereleases; null if the feed lacks it.</summary>
    public async Task<string?> LatestVersionAsync(string id, CancellationToken cancellationToken)
    {
        var versions = await feed.GetVersionsAsync(id, cancellationToken).ConfigureAwait(false);
        var ordered = versions.OrderBy(v => v, PackageVersion.Comparer).ToList();
        return ordered.LastOrDefault(v => !PackageVersion.IsPrerelease(v)) ?? ordered.LastOrDefault();
    }

    /// <summary>The unpacked package's folder, downloading it first if needed.</summary>
    public async Task<string> GetAsync(string id, string version, CancellationToken cancellationToken)
    {
        var lowerId = id.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();
        foreach (var candidate in new[] { Path.Combine(GlobalPackagesFolder, lowerId, lowerVersion), Path.Combine(folder, lowerId, lowerVersion) })
        {
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.nuspec").Length > 0)
            {
                return candidate;
            }
        }

        var target = Path.Combine(folder, lowerId, lowerVersion);
        var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            Directory.CreateDirectory(temp);
            await using (var stream = await feed.OpenPackageAsync(id, version, cancellationToken).ConfigureAwait(false))
            {
                // Only what the builder reads: the .nuspec and the lib folders.
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
                using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    var wanted = (name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !name.Contains('/', StringComparison.Ordinal))
                        || (name.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                    if (!wanted || name.Contains("..", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var path = Path.GetFullPath(Path.Combine(temp, name));
                    if (!path.StartsWith(Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    entry.ExtractToFile(path, overwrite: true);
                }
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(temp, target);
            return target;
        }
        catch (InvalidDataException ex)
        {
            throw new LibraryException($"{id} {version} is not a valid package.", ex);
        }
        catch (IOException ex)
        {
            throw new LibraryException($"Could not store {id} {version}: {ex.Message}", ex);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try
                {
                    Directory.Delete(temp, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>The package's .nuspec, read from its unpacked folder.</summary>
    public static Nuspec ReadNuspec(string packageFolder) =>
        Nuspec.Parse(File.ReadAllText(Directory.GetFiles(packageFolder, "*.nuspec").FirstOrDefault()
            ?? throw new LibraryException($"{packageFolder} has no .nuspec.")));

    /// <summary>
    /// The assemblies of the package's lib folder that suits the platform (not its
    /// satellites or design-time folders); empty if it has none for the platform.
    /// </summary>
    public static IReadOnlyList<string> Assemblies(string packageFolder, Core.ProjectPlatform platform)
    {
        var lib = Path.Combine(packageFolder, "lib");
        if (!Directory.Exists(lib))
        {
            return [];
        }

        var frameworks = Directory.GetDirectories(lib).Select(Path.GetFileName).OfType<string>().ToList();
        return TargetFramework.Best(frameworks, platform) is { } best
            ? Directory.GetFiles(Path.Combine(lib, best), "*.dll").Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];
    }
}
