using System.IO.Compression;
using StandaloneUiBuilder.Libraries;

namespace StandaloneUiBuilder.Core.Tests;

public class LibraryScannerTests
{
    private static LibraryControl Control(IReadOnlyList<LibraryControl> controls, string name) =>
        Assert.Single(controls, c => c.Name == name);

    [Fact]
    public void WpfControlsAreFoundAndHelpersSkipped()
    {
        var controls = SampleLibrary.Scan(ProjectPlatform.Wpf);
        Assert.Equal(["Badge", "BigRating", "Rating"], controls.Select(c => c.Name));
        var rating = Control(controls, "Rating");
        Assert.Equal("SampleControls.Wpf.Rating", rating.TypeName);
        Assert.Equal("StandaloneUiBuilder.SampleControls", rating.Assembly);
        Assert.Null(rating.TypeParameters);
    }

    [Fact]
    public void TheSettablePropertiesOfSimpleTypesAreOffered()
    {
        var rating = Control(SampleLibrary.Scan(ProjectPlatform.Wpf), "Rating");
        Assert.Equal(
            [
                ("Caption", "System.String", LibraryValueKind.Text),
                ("Stars", "System.Int32", LibraryValueKind.Whole),
                ("Value", "System.Double", LibraryValueKind.Number),
                ("ShowValue", "System.Boolean", LibraryValueKind.Flag),
                ("Shape", "SampleControls.Wpf.RatingShape", LibraryValueKind.Choice),
                ("Spacing", "System.Single", LibraryValueKind.Number),
                ("Votes", "System.Int64", LibraryValueKind.Whole),
            ],
            rating.Properties.Select(p => (p.Name, p.Type, p.Kind)));
        Assert.Equal(["Star", "Heart", "Circle"], rating.Properties.Single(p => p.Name == "Shape").Choices);
    }

    [Fact]
    public void InheritedPropertiesFollowTheControlsOwnAndContentComesFirst()
    {
        var controls = SampleLibrary.Scan(ProjectPlatform.Wpf);
        Assert.Equal(["Rows", "Caption", "Stars"], Control(controls, "BigRating").Properties.Take(3).Select(p => p.Name));
        Assert.Equal(["Content", "Shape"], Control(controls, "Badge").Properties.Select(p => p.Name));
    }

    [Fact]
    public void WinFormsControlsHaveTheirText()
    {
        var controls = SampleLibrary.Scan(ProjectPlatform.WinForms);
        var meter = Assert.Single(controls);
        Assert.Equal("SampleControls.WinForms.Meter", meter.TypeName);
        Assert.Equal(["Text", "Level", "Direction", "Zoom"], meter.Properties.Select(p => p.Name));
        Assert.Equal("System.Decimal", meter.Properties[^1].Type);
    }

    [Fact]
    public void BlazorComponentsOfferTheirParametersAndTypeArguments()
    {
        var controls = SampleLibrary.Scan(ProjectPlatform.Blazor);
        Assert.Equal(["Banner", "Picker"], controls.Select(c => c.Name));
        Assert.Equal(["Heading", "Tone", "Closable"], controls[0].Properties.Select(p => p.Name));
        Assert.Equal(["TValue"], controls[1].TypeParameters);
        Assert.Equal(["Placeholder"], controls[1].Properties.Select(p => p.Name));
    }

    [Theory]
    [InlineData(ProjectPlatform.WinUI)]
    [InlineData(ProjectPlatform.Maui)]
    public void AnAssemblyWithoutThePlatformsControlsHasNone(ProjectPlatform platform) =>
        Assert.Empty(SampleLibrary.Scan(platform));

    [Theory]
    [InlineData("net8.0-windows7.0", ProjectPlatform.Wpf, true)]
    [InlineData("net10.0-windows10.0.19041", ProjectPlatform.WinUI, true)]
    [InlineData("net8.0-windows7.0", ProjectPlatform.WinUI, false)]
    [InlineData("net8.0-android", ProjectPlatform.Maui, false)]
    [InlineData("net462", ProjectPlatform.Wpf, true)]
    [InlineData("net462", ProjectPlatform.Blazor, false)]
    [InlineData("net11.0", ProjectPlatform.Blazor, false)]
    [InlineData(".NETStandard2.0", ProjectPlatform.Blazor, true)]
    public void FrameworksSuitPlatforms(string framework, ProjectPlatform platform, bool usable) =>
        Assert.Equal(usable, TargetFramework.Score(framework, platform) is not null);

    [Fact]
    public void TheBestFrameworkIsTheNewestInThePlatformsFlavour()
    {
        string[] frameworks = ["net462", "net8.0", "net8.0-windows7.0", "net10.0-windows7.0", "netstandard2.0", "net10.0-android"];
        Assert.Equal("net10.0-windows7.0", TargetFramework.Best(frameworks, ProjectPlatform.Wpf));
        Assert.Equal("net8.0", TargetFramework.Best(frameworks, ProjectPlatform.Blazor));
    }

    [Theory]
    [InlineData("[1.2.0, )", "1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("[1.2.0]", "1.2.0")]
    [InlineData("(, 2.0)", null)]
    public void ADependencysVersionIsTheLowestItAllows(string range, string? version) =>
        Assert.Equal(version, Nuspec.LowestVersion(range));

    [Fact]
    public void VersionsCompareByNumberWithPrereleasesFirst()
    {
        string[] versions = ["1.10.0", "1.2.0", "1.2.0-beta", "2.0.0-rc.1", "1.9.9"];
        Assert.Equal(["1.2.0-beta", "1.2.0", "1.9.9", "1.10.0", "2.0.0-rc.1"], versions.OrderBy(v => v, PackageVersion.Comparer));
    }

    [Fact]
    public async Task APackageIsDownloadedScannedAndCached()
    {
        var root = Directory.CreateTempSubdirectory("uib-feed-").FullName;
        try
        {
            var feed = Path.Combine(root, "feed");
            Directory.CreateDirectory(feed);
            WritePackage(feed, "1.2.0");
            WritePackage(feed, "1.3.0-beta");
            var cache = new PackageCache(new FolderFeed(feed), Path.Combine(root, "cache"));
            var messages = new List<string>();

            var package = await LibraryLoader.LoadAsync(cache, SampleLibrary.PackageId, null, ProjectPlatform.Wpf, new SyncProgress(messages));
            Assert.Equal(SampleLibrary.PackageId, package.Id);
            Assert.Equal("1.2.0", package.Version);
            Assert.Equal(["Badge", "BigRating", "Rating"], package.Controls.Select(c => c.Name));
            Assert.Contains("Downloading Sample.Controls 1.2.0…", messages);

            // The skipped dependency is not fetched; the missing one does not stop the library.
            Assert.DoesNotContain(messages, m => m.Contains("Microsoft.Extensions", StringComparison.Ordinal));
            Assert.Contains("Downloading Missing.Package 1.0.0…", messages);

            // Stored: no need for the feed again.
            File.Delete(Path.Combine(feed, $"{SampleLibrary.PackageId}.1.2.0.nupkg"));
            var again = await LibraryLoader.LoadAsync(cache, SampleLibrary.PackageId, "1.2.0", ProjectPlatform.Blazor);
            Assert.Equal(["Banner", "Picker"], again.Controls.Select(c => c.Name));

            var none = await Assert.ThrowsAsync<LibraryException>(() => LibraryLoader.LoadAsync(cache, SampleLibrary.PackageId, "1.2.0", ProjectPlatform.Maui));
            Assert.Equal("Sample.Controls 1.2.0 has no .NET MAUI controls the builder can place.", none.Message);
            var missing = await Assert.ThrowsAsync<LibraryException>(() => LibraryLoader.LoadAsync(cache, "No.Such.Package", null, ProjectPlatform.Wpf));
            Assert.Equal("There is no package called No.Such.Package. Check the name on nuget.org.", missing.Message);
            await Assert.ThrowsAsync<LibraryException>(() => LibraryLoader.LoadAsync(cache, SampleLibrary.PackageId, null, ProjectPlatform.Any));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A .nupkg of the sample library, with a skipped and a missing dependency.</summary>
    private static void WritePackage(string feed, string version)
    {
        using var zip = ZipFile.Open(Path.Combine(feed, $"{SampleLibrary.PackageId}.{version}.nupkg"), ZipArchiveMode.Create);
        var nuspec = zip.CreateEntry($"{SampleLibrary.PackageId}.nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{SampleLibrary.PackageId}</id>
                    <version>{version}</version>
                    <dependencies>
                      <group targetFramework="net10.0-windows7.0">
                        <dependency id="Microsoft.Extensions.Logging" version="10.0.0" />
                        <dependency id="Missing.Package" version="[1.0.0, )" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);
        }

        zip.CreateEntryFromFile(SampleLibrary.AssemblyPath, "lib/net10.0-windows7.0/StandaloneUiBuilder.SampleControls.dll");
        zip.CreateEntryFromFile(SampleLibrary.AssemblyPath, "lib/net10.0-windows7.0/Design/Ignored.dll");

        // As a Blazor library would have it: plain .NET, which WPF uses only if there is nothing better.
        zip.CreateEntryFromFile(SampleLibrary.AssemblyPath, "lib/net10.0/StandaloneUiBuilder.SampleControls.dll");
    }

    /// <summary>Reports straight away, unlike Progress, which posts to a synchronisation context.</summary>
    private sealed class SyncProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }
}
