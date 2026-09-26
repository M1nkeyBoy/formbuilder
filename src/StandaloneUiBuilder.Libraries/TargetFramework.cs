using System.Text.RegularExpressions;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Libraries;

/// <summary>
/// Chooses which of a package's target frameworks (its lib folders and dependency groups)
/// suits a platform: the newest .NET the builder's exports can use, preferring the platform's
/// own flavour (net8.0-windows for WPF, plain net8.0 for Blazor).
/// </summary>
public static partial class TargetFramework
{
    /// <summary>The newest .NET the exported projects target.</summary>
    public const int NewestNet = 10;

    [GeneratedRegex(@"^net(?<major>\d+)\.(?<minor>\d+)(-(?<os>[a-z]+)(?<osVersion>[\d.]*))?$")]
    private static partial Regex ModernNet();

    [GeneratedRegex(@"^netcoreapp(?<version>[\d.]+)$")]
    private static partial Regex NetCoreApp();

    [GeneratedRegex(@"^netstandard(?<version>[\d.]+)$")]
    private static partial Regex NetStandard();

    [GeneratedRegex(@"^net(?<version>4\d+)$")]
    private static partial Regex NetFramework();

    /// <summary>A .nuspec framework name in the lib folder's form: ".NETStandard2.0" becomes "netstandard2.0".</summary>
    public static string Normalize(string framework)
    {
        var f = framework.Trim().ToLowerInvariant();
        if (f.StartsWith(".netframework", StringComparison.Ordinal))
        {
            return "net" + f[".netframework".Length..].Replace(".", "", StringComparison.Ordinal);
        }

        if (f.StartsWith(".netstandard", StringComparison.Ordinal))
        {
            return "netstandard" + f[".netstandard".Length..];
        }

        if (f.StartsWith(".netcoreapp", StringComparison.Ordinal))
        {
            var version = f[".netcoreapp".Length..];
            return version.StartsWith('1') || version.StartsWith('2') || version.StartsWith('3') ? "netcoreapp" + version : "net" + version;
        }

        return f;
    }

    /// <summary>How well a framework suits the platform: higher is better; null if it cannot be used.</summary>
    public static int? Score(string framework, ProjectPlatform platform)
    {
        var f = Normalize(framework);
        if (ModernNet().Match(f) is { Success: true } modern)
        {
            var major = int.Parse(modern.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var os = modern.Groups["os"].Value;
            if (major > NewestNet || major < 5)
            {
                return null;
            }

            var flavour = (platform, os) switch
            {
                (_, "") => platform is ProjectPlatform.Blazor or ProjectPlatform.Maui ? 3 : 1,
                (ProjectPlatform.Wpf or ProjectPlatform.WinForms, "windows") => 3,
                (ProjectPlatform.WinUI, "windows") => 3,
                (ProjectPlatform.Maui, "windows") => 2,
                (ProjectPlatform.Blazor, "browser") => 2,
                _ => (int?)null,
            };

            // WinUI needs a Windows 10 flavour; WPF and Windows Forms work with any Windows one.
            if (flavour is not { } points || (platform == ProjectPlatform.WinUI && os == "windows" && !modern.Groups["osVersion"].Value.StartsWith("10", StringComparison.Ordinal)))
            {
                return os.Length == 0 ? 1 : null;
            }

            return 1000 + major * 10 + points;
        }

        if (NetCoreApp().IsMatch(f))
        {
            return platform is ProjectPlatform.Wpf or ProjectPlatform.WinForms or ProjectPlatform.Blazor ? 500 : null;
        }

        if (NetStandard().Match(f) is { Success: true } standard)
        {
            return 300 + (int)(double.Parse(standard.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture) * 10);
        }

        // .NET Framework libraries often still work for WPF and Windows Forms on .NET.
        if (NetFramework().IsMatch(f))
        {
            return platform is ProjectPlatform.Wpf or ProjectPlatform.WinForms ? 100 : null;
        }

        return null;
    }

    /// <summary>The best of several frameworks for the platform, or null if none can be used.</summary>
    public static string? Best(IEnumerable<string> frameworks, ProjectPlatform platform) =>
        frameworks.Select(f => (Framework: f, Score: Score(f, platform)))
            .Where(f => f.Score is not null)
            .OrderByDescending(f => f.Score)
            .Select(f => f.Framework)
            .FirstOrDefault();
}
