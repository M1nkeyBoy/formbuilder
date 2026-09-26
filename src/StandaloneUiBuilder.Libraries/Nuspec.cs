using System.Xml.Linq;

namespace StandaloneUiBuilder.Libraries;

/// <summary>What the builder needs from a package's .nuspec: its ID, version and dependencies.</summary>
public sealed record Nuspec(string Id, string Version, IReadOnlyList<DependencyGroup> Groups)
{
    public static Nuspec Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new LibraryException("The package has no .nuspec.");
        var metadata = root.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
            ?? throw new LibraryException("The package's .nuspec has no metadata.");
        string Value(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";

        var groups = new List<DependencyGroup>();
        if (metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies") is { } dependencies)
        {
            var grouped = dependencies.Elements().Where(e => e.Name.LocalName == "group").ToList();
            if (grouped.Count == 0)
            {
                groups.Add(new DependencyGroup(null, Dependencies(dependencies)));
            }

            foreach (var group in grouped)
            {
                var framework = (string?)group.Attribute("targetFramework");
                groups.Add(new DependencyGroup(string.IsNullOrWhiteSpace(framework) ? null : TargetFramework.Normalize(framework), Dependencies(group)));
            }
        }

        return new Nuspec(Value("id"), Value("version"), groups);
    }

    private static IReadOnlyList<Dependency> Dependencies(XElement parent) =>
        parent.Elements().Where(e => e.Name.LocalName == "dependency")
            .Select(e => new Dependency((string?)e.Attribute("id") ?? "", LowestVersion((string?)e.Attribute("version"))))
            .Where(d => d.Id.Length > 0)
            .ToList();

    /// <summary>The lowest version a range allows, which NuGet itself chooses: "[1.2, )" and "1.2" give 1.2.</summary>
    public static string? LowestVersion(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return null;
        }

        var lower = range.Trim().TrimStart('[', '(').Split(',')[0].Trim().TrimEnd(']', ')');
        return lower.Length > 0 ? lower : null;
    }
}

/// <summary>Dependencies for one target framework, or for all (Framework null).</summary>
public sealed record DependencyGroup(string? Framework, IReadOnlyList<Dependency> Dependencies);

/// <summary>A dependency and the version to use; null when the package names none.</summary>
public sealed record Dependency(string Id, string? Version);
