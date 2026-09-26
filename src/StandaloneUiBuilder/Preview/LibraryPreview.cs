using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Libraries;

namespace StandaloneUiBuilder.Preview;

/// <summary>
/// Draws WPF and Windows Forms library controls on the canvas as they look in a running app.
/// The drawing is done by the preview host (<see cref="PreviewHost"/>), a process of its own;
/// until a drawing arrives, and for other platforms, the canvas shows a labelled box. Drawings
/// are kept by everything that affects them: the control's type, values, size and theme.
/// </summary>
internal sealed class LibraryPreview : IDisposable
{
    /// <summary>How long a control may take to draw before the host is restarted.</summary>
    private static readonly TimeSpan DrawTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<ProjectDocument> document;
    private readonly Dispatcher dispatcher;
    private readonly Dictionary<string, Drawing> drawings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<IReadOnlyList<string>>> assemblies = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim hostLock = new(1, 1);
    private readonly PackageCache cache = LibrariesWindow.CreateCache();
    private Process? host;
    private int nextId;
    private bool disposed;

    private sealed class Drawing
    {
        public BitmapSource? Image { get; set; }

        public string? Error { get; set; }
    }

    public LibraryPreview(Func<ProjectDocument> document, Dispatcher dispatcher)
    {
        this.document = document;
        this.dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread when a drawing arrives, so the canvas can show it.</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// The control's drawing if there is one; otherwise null, asking the host for it. A control
    /// that cannot be drawn stays a labelled box, with the reason as its tooltip.
    /// </summary>
    public FrameworkElement? Draw(ControlDocument control)
    {
        var project = document();
        if (disposed || project.Platform is not (ProjectPlatform.Wpf or ProjectPlatform.WinForms)
            || control.Width <= 0 || control.Height <= 0
            || LibraryValues.PackageOf(project, control.Properties.LibraryType) is not { } package)
        {
            return null;
        }

        var (key, dark) = Key(project, control, package);
        if (drawings.TryGetValue(key, out var drawing))
        {
            return drawing.Image is { } image
                ? new Image { Source = image, Stretch = Stretch.Fill, Width = control.Width, Height = control.Height }
                : null;
        }

        drawings[key] = new Drawing();
        var request = (project.Platform, control, dark, package, keys: (IReadOnlyDictionary<string, string>)(project.LicenseKeys ?? System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty));
        _ = Task.Run(async () =>
        {
            var (image, error) = await DrawAsync(request.Platform, request.control, request.dark, request.package, request.keys);
            await dispatcher.InvokeAsync(() =>
            {
                drawings[key] = new Drawing { Image = image, Error = error };
                if (image is not null)
                {
                    Updated?.Invoke(this, EventArgs.Empty);
                }
            });
        });
        return null;
    }

    /// <summary>Why a control is not drawn, if the host could not draw it.</summary>
    public string? ErrorFor(ControlDocument control)
    {
        var project = document();
        return LibraryValues.PackageOf(project, control.Properties.LibraryType) is { } package
            && drawings.TryGetValue(Key(project, control, package).Key, out var drawing) ? drawing.Error : null;
    }

    /// <summary>Everything a drawing depends on.</summary>
    private static (string Key, bool Dark) Key(ProjectDocument project, ControlDocument control, LibraryPackage package)
    {
        var dark = project.Theme == ProjectTheme.Dark || (project.Theme == ProjectTheme.System && Design.DesignSurface.WindowsAppsUseDarkMode());
        var key = string.Join("|", project.Platform, package.Id, package.Version, control.Properties.LibraryType, control.Width, control.Height, dark,
            JsonSerializer.Serialize(control.Properties.LibrarySettings ?? []));
        return (key, dark);
    }

    private async Task<(BitmapSource? Image, string? Error)> DrawAsync(
        ProjectPlatform platform, ControlDocument control, bool dark, LibraryPackage package, IReadOnlyDictionary<string, string> keys)
    {
        try
        {
            var paths = await AssembliesFor(package, platform);
            var request = new PreviewRequest(
                Interlocked.Increment(ref nextId), platform, control.Properties.LibraryType!, control.Properties.LibraryAssembly!,
                control.Properties.LibrarySettings ?? [], control.Width, control.Height, dark, paths, keys);
            var response = await SendAsync(request);
            if (response.Png is not { } png)
            {
                return (null, response.Error ?? "The control could not be drawn.");
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(Convert.FromBase64String(png));
            bitmap.EndInit();
            bitmap.Freeze();
            return (bitmap, null);
        }
        catch (Exception ex) when (ex is LibraryException or IOException or InvalidOperationException or TimeoutException or JsonException
            or System.Net.Http.HttpRequestException or FormatException or NotSupportedException)
        {
            return (null, ex.Message);
        }
    }

    private Task<IReadOnlyList<string>> AssembliesFor(LibraryPackage package, ProjectPlatform platform)
    {
        var key = $"{platform}|{package.Id}|{package.Version}";
        lock (assemblies)
        {
            if (!assemblies.TryGetValue(key, out var task) || task.IsFaulted)
            {
                task = LibraryLoader.AssembliesAsync(cache, package, platform);
                assemblies[key] = task;
            }

            return task;
        }
    }

    /// <summary>Sends one request to the host, starting it if needed; one request at a time.</summary>
    private async Task<PreviewResponse> SendAsync(PreviewRequest request)
    {
        await hostLock.WaitAsync();
        try
        {
            var process = host is { HasExited: false } running ? running : StartHost();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, PreviewHost.Json));
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(DrawTimeout);
            while (true)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    StopHost();
                    throw new TimeoutException("The control took too long to draw.");
                }

                if (line is null)
                {
                    StopHost();
                    throw new InvalidOperationException("The control stopped the preview while drawing.");
                }

                if (JsonSerializer.Deserialize<PreviewResponse>(line, PreviewHost.Json) is { } response && response.Id == request.Id)
                {
                    return response;
                }
            }
        }
        finally
        {
            hostLock.Release();
        }
    }

    private Process StartHost()
    {
        StopHost();
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("The editor's program is not known."), PreviewHost.Argument)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        host = Process.Start(start) ?? throw new InvalidOperationException("The preview could not start.");
        return host;
    }

    private void StopHost()
    {
        try
        {
            if (host is { HasExited: false })
            {
                host.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        host?.Dispose();
        host = null;
    }

    public void Dispose()
    {
        disposed = true;
        try
        {
            host?.StandardInput.Close();
            if (host is not null && !host.WaitForExit(2000))
            {
                StopHost();
            }
        }
        catch (InvalidOperationException)
        {
        }

        host?.Dispose();
    }
}
