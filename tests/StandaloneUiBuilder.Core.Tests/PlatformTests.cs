using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

public class PlatformTests
{
    [Fact]
    public void TheFileStoresAPlatformButNotAnyPlatform()
    {
        var document = ProjectDocument.CreateBlank();
        Assert.Equal(ProjectPlatform.Any, document.Platform);
        Assert.DoesNotContain("\"platform\"", ProjectFile.Serialize(document));

        var json = ProjectFile.Serialize(ProjectDocument.CreateBlank(ProjectPlatform.WinUI));
        Assert.Contains("\"platform\": \"WinUI\"", json);
        Assert.Equal(ProjectPlatform.WinUI, ProjectFile.Deserialize(json).Platform);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json.Replace("\"WinUI\"", "\"Qt\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void ChangingThePlatformCanBeUndone()
    {
        var editor = new DesignEditor();
        editor.New(ProjectPlatform.Wpf);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SetPlatform(ProjectPlatform.Wpf));
        Assert.True(editor.SetPlatform(ProjectPlatform.Blazor));
        Assert.Equal(ProjectPlatform.Blazor, editor.Document.Platform);
        editor.Undo();
        Assert.Equal(ProjectPlatform.Wpf, editor.Document.Platform);
    }

    public static TheoryData<ProjectPlatform> Platforms => [.. ProjectPlatforms.All.Where(p => p != ProjectPlatform.Any)];

    [Theory]
    [MemberData(nameof(Platforms))]
    public void AProjectForOnePlatformExportsOnlyToIt(ProjectPlatform platform)
    {
        var document = ProjectDocument.CreateBlank(platform);
        var checks = new Dictionary<ProjectPlatform, Func<ProjectDocument, IReadOnlyList<string>>>
        {
            [ProjectPlatform.Wpf] = WpfGenerator.Check,
            [ProjectPlatform.WinForms] = WinFormsGenerator.Check,
            [ProjectPlatform.WinUI] = WinUIGenerator.Check,
            [ProjectPlatform.Maui] = MauiGenerator.Check,
            [ProjectPlatform.Blazor] = BlazorGenerator.Check,
        };

        foreach (var (target, check) in checks)
        {
            var problems = check(document);
            if (target == platform)
            {
                Assert.Empty(problems);
            }
            else
            {
                Assert.Equal($"The project is for {platform.DisplayName()}, so it exports to {platform.DisplayName()} only. Project > Platform changes it.", Assert.Single(problems));
            }

            Assert.Empty(check(document with { Platform = ProjectPlatform.Any }));
        }
    }
}
