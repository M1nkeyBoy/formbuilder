using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media.Imaging;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Preview;

namespace StandaloneUiBuilder.UiTests;

/// <summary>The preview host, the editor's own program in a process of its own, draws library controls.</summary>
public sealed class PreviewHostTests
{
    private static string SampleAssembly => Path.Combine(AppContext.BaseDirectory, "StandaloneUiBuilder.SampleControls.dll");

    [WindowsFact]
    public void TheHostDrawsWpfAndWinFormsControlsAndSurvivesBadOnes()
    {
#if RELEASE
        const string configuration = "Release";
#else
        const string configuration = "Debug";
#endif
        var exe = Path.Combine(EditorSession.RepositoryRoot, "src", "StandaloneUiBuilder", "bin", configuration, "net10.0-windows", "StandaloneUiBuilder.exe");
        using var host = Process.Start(new ProcessStartInfo(exe, PreviewHost.Argument)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            var badge = Send(host, new PreviewRequest(1, ProjectPlatform.Wpf, "SampleControls.Wpf.Badge", "StandaloneUiBuilder.SampleControls",
                [new LibrarySetting { Name = "Content", Type = "System.String", Value = "Hello" }, new LibrarySetting { Name = "Shape", Type = "SampleControls.Wpf.RatingShape", Value = "Heart" }],
                120, 30, Dark: false, [SampleAssembly], new Dictionary<string, string>()));
            Assert.Null(badge.Error);
            var picture = Decode(badge.Png!);
            Assert.Equal((240, 60), (picture.PixelWidth, picture.PixelHeight));
            Assert.True(HasInk(picture), "The badge's text was not drawn.");
            File.WriteAllBytes(Path.Combine(EditorSession.ArtifactsDirectory, "40-preview-badge.png"), Convert.FromBase64String(badge.Png!));

            var meter = Send(host, new PreviewRequest(2, ProjectPlatform.WinForms, "SampleControls.WinForms.Meter", "StandaloneUiBuilder.SampleControls",
                [new LibrarySetting { Name = "Level", Type = "System.Int32", Value = "3" }], 100, 40, Dark: false, [SampleAssembly], new Dictionary<string, string>()));
            Assert.Null(meter.Error);
            Assert.Equal((100, 40), (Decode(meter.Png!).PixelWidth, Decode(meter.Png!).PixelHeight));

            // A type the library does not have: an error, and the host carries on.
            var missing = Send(host, new PreviewRequest(3, ProjectPlatform.Wpf, "SampleControls.Wpf.Nothing", "StandaloneUiBuilder.SampleControls",
                [], 100, 40, Dark: false, [SampleAssembly], new Dictionary<string, string>()));
            Assert.Null(missing.Png);
            Assert.Contains("Nothing", missing.Error);
            Assert.NotNull(Send(host, new PreviewRequest(4, ProjectPlatform.Wpf, "SampleControls.Wpf.Badge", "StandaloneUiBuilder.SampleControls",
                [], 50, 20, Dark: true, [SampleAssembly], new Dictionary<string, string>())).Png);

            // Closing its input ends it.
            host.StandardInput.Close();
            Assert.True(host.WaitForExit(10_000), "The host did not exit when its input closed.");
        }
        finally
        {
            if (!host.HasExited)
            {
                host.Kill();
            }
        }
    }

    private static PreviewResponse Send(Process host, PreviewRequest request)
    {
        host.StandardInput.WriteLine(JsonSerializer.Serialize(request, PreviewHost.Json));
        host.StandardInput.Flush();
        var line = host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
        var response = JsonSerializer.Deserialize<PreviewResponse>(line!, PreviewHost.Json)!;
        Assert.Equal(request.Id, response.Id);
        return response;
    }

    private static BitmapSource Decode(string png) =>
        BitmapFrame.Create(new MemoryStream(Convert.FromBase64String(png)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <summary>True if any pixel is not fully transparent.</summary>
    private static bool HasInk(BitmapSource picture)
    {
        var converted = new FormatConvertedBitmap(picture, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return true;
            }
        }

        return false;
    }
}
