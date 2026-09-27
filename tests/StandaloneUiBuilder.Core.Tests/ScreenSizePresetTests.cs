namespace StandaloneUiBuilder.Core.Tests;

public class ScreenSizePresetTests
{
    [Fact]
    public void EveryPresetIsAValidScreenSize()
    {
        foreach (var preset in ScreenSizePresets.All)
        {
            var editor = new DesignEditor();
            Assert.Null(editor.SetScreenSize(preset.Width, preset.Height));
        }

        Assert.Equal(ScreenSizePresets.All.Count, ScreenSizePresets.All.Select(p => p.Name).Distinct().Count());
        Assert.Equal([ScreenSizePresets.Phones, ScreenSizePresets.Tablets, ScreenSizePresets.Desktops], ScreenSizePresets.All.Select(p => p.Category).Distinct());
    }

    [Fact]
    public void ASizeFindsItsPresetOrNone()
    {
        Assert.Equal("iPhone SE", ScreenSizePresets.Find(375, 667)!.Name);
        Assert.Equal("Full HD 1080p", ScreenSizePresets.Find(1920, 1080)!.Name);
        Assert.Equal("Default window", ScreenSizePresets.Find(ScreenDocument.DefaultWidth, ScreenDocument.DefaultHeight)!.Name);
        Assert.Null(ScreenSizePresets.Find(376, 667));
        Assert.Equal("iPhone SE (375 × 667)", ScreenSizePresets.Find(375, 667)!.Label);
    }
}
