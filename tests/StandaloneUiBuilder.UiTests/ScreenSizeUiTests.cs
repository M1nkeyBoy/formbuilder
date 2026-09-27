using FlaUI.Core.AutomationElements;

namespace StandaloneUiBuilder.UiTests;

/// <summary>The Screen section's Device list sets the canvas to a device's size; a size of your own shows Custom.</summary>
public sealed class ScreenSizeUiTests : IDisposable
{
    private readonly string recoveryDirectory = Directory.CreateTempSubdirectory("uib-recovery-").FullName;

    public void Dispose() => Directory.Delete(recoveryDirectory, recursive: true);

    [UiWalkthroughFact]
    public void ChoosingADeviceSizesTheCanvasAndOwnSizesAreCustom()
    {
        using var session = EditorSession.Launch(recoveryDirectory);
        var devices = session.ById("ScreenSizeBox").AsComboBox();
        Assert.Equal("Default window (800 × 600)", devices.SelectedItem?.Name);

        devices.Select("iPhone SE (375 × 667)");
        EditorSession.WaitUntil(() => session.Field("ScreenWidthBox").Text == "375" && session.Field("ScreenHeightBox").Text == "667",
            () => $"Size: {session.Field("ScreenWidthBox").Text} × {session.Field("ScreenHeightBox").Text}");
        var surface = session.ById("Surface").BoundingRectangle;
        Assert.Equal((375, 667), (surface.Width, surface.Height));
        session.Screenshot("41-device-size");

        // Typing a size of your own shows Custom; a desktop size is found again.
        session.TypeInto("ScreenWidthBox", "400");
        EditorSession.WaitUntil(() => devices.SelectedItem?.Name == "Custom", () => $"Device: {devices.SelectedItem?.Name}");
        devices.Select("Full HD 1080p (1920 × 1080)");
        EditorSession.WaitUntil(() => session.Field("ScreenWidthBox").Text == "1920", () => $"Width: {session.Field("ScreenWidthBox").Text}");
        Assert.Equal("Full HD 1080p (1920 × 1080)", devices.SelectedItem?.Name);
    }
}
