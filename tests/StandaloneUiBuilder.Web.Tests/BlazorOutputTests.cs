using Microsoft.Playwright;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Blazor;

namespace StandaloneUiBuilder.Web.Tests;

/// <summary>
/// Builds the exported samples, runs them and checks in a browser where every control really
/// is, on every screen, at the design size and (for a resizable screen) larger, against the
/// Core layout rules. Also follows the layout demo's buttons from screen to screen.
/// </summary>
public sealed class BlazorOutputTests
{
    private static ProjectDocument Sample(string name) =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", name + ".uibproj"));

    [WebFact]
    public Task CustomerFormLaysOutByItsAnchors() => AssertLayout(Sample("customer-form"));

    [WebFact]
    public Task LayoutDemoLaysOutContainersOnEveryScreen() => AssertLayout(Sample("layout-demo"));

    [WebFact]
    public async Task StylesFromTheDesignAreApplied()
    {
        using var app = await GeneratedApp.StartAsync(Sample("layout-demo"), BlazorExporter.Export);
        await using var browser = await LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(app.Url);

        var header = await page.EvaluateAsync<string[]>("() => { const s = getComputedStyle(document.getElementById('HeaderLabel')); return [s.fontSize, s.fontWeight, s.color]; }");
        var ok = await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('OkButton')).backgroundColor");

        Assert.Equal(["16px", "700", "rgb(30, 78, 140)"], header);
        Assert.Equal("rgb(30, 111, 217)", ok);

        // The logo's picture is served and decoded: it has its natural size.
        var logo = await page.EvaluateAsync<double[]>("async () => { const i = document.getElementById('LogoImage'); await i.decode(); return [i.naturalWidth, i.naturalHeight]; }");
        Assert.Equal([96.0, 64.0], logo);
    }

    [WebFact]
    public async Task ButtonsOpenAndCloseScreens()
    {
        var document = Sample("layout-demo");
        using var app = await GeneratedApp.StartAsync(document, BlazorExporter.Export);
        await using var browser = await LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 900, Height = 700 } });
        await page.GotoAsync(app.Url);

        // The page works once its interactive connection is up; keep clicking until then.
        await ClickUntilAsync(page, "#OkButton", () => page.Url.EndsWith("/settings", StringComparison.Ordinal));
        await page.Locator("#CloseSettingsButton").WaitForAsync();
        await Screenshot(page, "30-blazor-settings");
        await ClickUntilAsync(page, "#CloseSettingsButton", () => page.Url == app.Url + "/");
        await page.Locator("#OkButton").WaitForAsync();
    }

    [WebFact]
    public async Task ChoosingARadioButtonClearsTheOthersInItsGroup()
    {
        var document = Sample("layout-demo");
        using var app = await GeneratedApp.StartAsync(document, BlazorExporter.Export);
        await using var browser = await LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(app.Url + "/settings");

        var fast = page.Locator("#FastRadio input");
        var safe = page.Locator("#SafeRadio input");
        Assert.True(await fast.IsCheckedAsync());
        await ClickUntilAsync(page, "#SafeRadio input", async () => await safe.IsCheckedAsync());
        Assert.False(await fast.IsCheckedAsync());
    }

    [WebFact]
    public async Task ChoosingATabShowsItsPage()
    {
        var document = Sample("layout-demo");
        var screen = document.Screens[1];
        using var app = await GeneratedApp.StartAsync(document, BlazorExporter.Export);
        await using var browser = await LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = screen.Width, Height = screen.Height } });
        await page.GotoAsync(app.Url + "/settings");

        await ClickUntilAsync(page, "#DetailsTabs .uib-tab:nth-child(2)", async () => await page.Locator("#AdvancedPage").IsVisibleAsync());
        await Screenshot(page, "31-blazor-LayoutDemo-Settings-advanced-tab");

        // Now laid out as if the design showed the second tab.
        var tabs = ControlTree.All(screen.Controls).Single(c => c.Name == "DetailsTabs");
        var shown = screen with
        {
            Controls = ControlTree.Replace(screen.Controls, tabs.Id, t => t with { Properties = t.Properties with { SelectedTab = 1 } }),
        };
        await AssertPlacedAsync(page, shown, screen.Width, screen.Height);
        Assert.False(await page.Locator("#NotesTextBox").IsVisibleAsync());
    }

    [WebFact]
    public async Task TabFollowsTheDesignsTabOrder()
    {
        var document = Sample("layout-demo");
        var screen = document.Screens[1];
        using var app = await GeneratedApp.StartAsync(document, BlazorExporter.Export);
        await using var browser = await LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = screen.Width, Height = screen.Height } });
        await page.GotoAsync(app.Url + "/settings");
        await page.Locator("#ServerTextBox").WaitForAsync();

        // Radio buttons are one stop for the group in a browser, and hidden pages have none.
        var hidden = ContainerLayout.Flatten(screen).Where(p => p.IsHidden).Select(p => p.Control.Id).ToHashSet();
        var expected = TabSequence.Resolve(screen)
            .Where(c => c.Type != ControlType.RadioButton && !hidden.Contains(c.Id))
            .Select(c => c.Name).ToList();
        var names = expected.ToHashSet();

        var visited = new List<string>();
        for (var i = 0; i < expected.Count * 4 && visited.Count(names.Contains) < expected.Count + 1; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var id = await page.EvaluateAsync<string?>("() => document.activeElement?.closest('[id]')?.id ?? null");
            if (id is not null && names.Contains(id) && (visited.Count == 0 || visited[^1] != id))
            {
                visited.Add(id);
            }
        }

        var start = visited.IndexOf(expected[0]);
        Assert.True(start >= 0 && visited.Count >= start + expected.Count, $"Tab visited {string.Join(", ", visited)}");
        Assert.Equal(expected, visited.Skip(start).Take(expected.Count));
    }

    private static async Task AssertLayout(ProjectDocument document)
    {
        using var app = await GeneratedApp.StartAsync(document, BlazorExporter.Export);
        await using var browser = await LaunchAsync();
        foreach (var screen in document.Screens)
        {
            var sizes = new List<(int Width, int Height)> { (screen.Width, screen.Height) };
            if (AnchorLayout.IsResizable(screen))
            {
                sizes.Add((screen.Width + 230, screen.Height + 140));
            }

            foreach (var (width, height) in sizes)
            {
                var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = height } });
                await page.GotoAsync(app.Url + BlazorGenerator.Route(document, screen));
                await Screenshot(page, $"31-blazor-{CodeName(document)}-{screen.Name}-{width}x{height}");

                await AssertPlacedAsync(page, screen, width, height);
                await page.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Every control shown is where the Core layout puts it; controls on tab pages that are not
    /// shown take no space.
    /// </summary>
    private static async Task AssertPlacedAsync(IPage page, ScreenDocument screen, int width, int height)
    {
        var placedControls = ContainerLayout.Flatten(screen, width, height);
        var boxes = await BoxesAsync(page, placedControls.Select(p => p.Control.Name).ToArray());
        for (var i = 0; i < placedControls.Count; i++)
        {
            var placed = placedControls[i];
            var actual = boxes[i] ?? throw new InvalidOperationException($"{placed.Control.Name} is not on the {screen.Name} page.");
            var what = $"{screen.Name}: {placed.Control.Name} ({placed.Control.Type}, depth {placed.Depth}) at {width} × {height}";
            if (placed.IsHidden)
            {
                Assert.True(actual.Width == 0 && actual.Height == 0, $"{what} is on a hidden tab page but has {actual}");
                continue;
            }

            var expected = placed.Bounds;

            // Browsers lay out shared grid tracks in fractions of a pixel; allow one.
            bool Near(int a, int b) => Math.Abs(a - b) <= 1;
            Assert.True(
                Near(expected.X, actual.X) && Near(expected.Y, actual.Y) && Near(expected.Width, actual.Width) && Near(expected.Height, actual.Height),
                $"{what}: page has {actual}, expected {expected}");
        }
    }

    /// <summary>
    /// Where elements are on the page, in CSS pixels from its top-left, read in one go from the
    /// browser's own layout; null for an element that is missing.
    /// </summary>
    private static async Task<ControlBounds?[]> BoxesAsync(IPage page, string[] ids)
    {
        var rects = await page.EvaluateAsync<double[]?[]>(
            "ids => ids.map(id => { const e = document.getElementById(id); if (!e) return null; const r = e.getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; })",
            ids);
        return rects.Select(r => r is null ? (ControlBounds?)null
            : new ControlBounds((int)Math.Round(r[0]), (int)Math.Round(r[1]), (int)Math.Round(r[2]), (int)Math.Round(r[3]))).ToArray();
    }

    private static string CodeName(ProjectDocument document) => Output.CodeNames.ToNamespace(document.Name);

    private static async Task ClickUntilAsync(IPage page, string selector, Func<bool> done) =>
        await ClickUntilAsync(page, selector, () => Task.FromResult(done()));

    private static async Task ClickUntilAsync(IPage page, string selector, Func<Task<bool>> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await done())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Clicking {selector} had no effect; the page is at {page.Url}.");
            await page.Locator(selector).ClickAsync();
            await Task.Delay(300);
        }
    }

    /// <summary>
    /// Edge, installed on Windows, or the Chromium at UIB_CHROMIUM (or Playwright's usual
    /// location), so the tests never download a browser.
    /// </summary>
    private static async Task<BrowserHandle> LaunchAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var chromium = Environment.GetEnvironmentVariable("UIB_CHROMIUM") is { Length: > 0 } path ? path
            : File.Exists("/opt/pw-browsers/chromium") ? "/opt/pw-browsers/chromium"
            : null;
        var options = OperatingSystem.IsWindows() && chromium is null
            ? new BrowserTypeLaunchOptions { Channel = "msedge" }
            : new BrowserTypeLaunchOptions { ExecutablePath = chromium };
        return new BrowserHandle(playwright, await playwright.Chromium.LaunchAsync(options));
    }

    private static async Task Screenshot(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("UIB_UI_TEST_ARTIFACTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, name + ".png") });
        }
    }

    /// <summary>A browser and the Playwright instance that owns it, closed together.</summary>
    private sealed class BrowserHandle(IPlaywright playwright, IBrowser browser) : IAsyncDisposable
    {
        public Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null) => browser.NewPageAsync(options);

        public async ValueTask DisposeAsync()
        {
            await browser.CloseAsync();
            playwright.Dispose();
        }
    }
}
