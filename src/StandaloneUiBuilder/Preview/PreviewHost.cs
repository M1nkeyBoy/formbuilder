using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Preview;

/// <summary>
/// Draws library controls for the editor, in a process of its own: the editor starts its own
/// program with <see cref="Argument"/>, writes one <see cref="PreviewRequest"/> per line to its
/// standard input and reads one <see cref="PreviewResponse"/> per line back. A library's code
/// runs only here, so a control that throws, hangs or crashes cannot take the editor or the
/// design with it. The process ends when the editor closes its input.
/// </summary>
internal static class PreviewHost
{
    public const string Argument = "--preview-host";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Scale of the drawing: twice the design size, so it stays sharp when zoomed in.</summary>
    private const int Scale = 2;

    private static readonly Dictionary<string, string> AssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RegisteredLicenses = new(StringComparer.Ordinal);
    private static Window? stage;
    private static bool? stageDark;
    private static bool visualStyles;

    public static void Run(Application app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.DispatcherUnhandledException += (_, e) =>
        {
            // A library's exception on the dispatcher: report it, and carry on.
            e.Handled = true;
        };
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            name.Name is { } simple && AssemblyPaths.TryGetValue(simple, out var path) ? context.LoadFromAssemblyPath(path) : null;

        var output = Console.Out;
        var input = Console.In;
        var dispatcher = app.Dispatcher;
        var reader = new Thread(() =>
        {
            while (input.ReadLine() is { } line)
            {
                PreviewRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<PreviewRequest>(line, Json);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (request is null)
                {
                    continue;
                }

                var response = dispatcher.Invoke(() => Draw(request)).GetAwaiter().GetResult();
                lock (output)
                {
                    output.WriteLine(JsonSerializer.Serialize(response, Json));
                    output.Flush();
                }
            }

            dispatcher.InvokeShutdown();
        })
        {
            IsBackground = true,
            Name = "Preview requests",
        };
        reader.Start();
    }

    private static async Task<PreviewResponse> Draw(PreviewRequest request)
    {
        try
        {
            foreach (var path in request.Assemblies)
            {
                AssemblyPaths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }

            RegisterLicenses(request.LicenseKeys);
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(request.Assembly));
            var type = assembly.GetType(request.TypeName, throwOnError: true)!;
            var png = request.Platform == ProjectPlatform.WinForms
                ? DrawWinForms(type, request)
                : await DrawWpf(type, request);
            return new PreviewResponse(request.Id, Convert.ToBase64String(png), null);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } cause } ? cause : ex;
            return new PreviewResponse(request.Id, null, $"{inner.GetType().Name}: {inner.Message}");
        }
    }

    /// <summary>Draws a WPF control in a window off the screen, as it looks in a running app.</summary>
    private static async Task<byte[]> DrawWpf(Type type, PreviewRequest request)
    {
        var element = (FrameworkElement)Activator.CreateInstance(type)!;
        Apply(element, request.Settings);
        element.Width = request.Width;
        element.Height = request.Height;
        var root = new Grid { Width = request.Width, Height = request.Height, Background = Brushes.Transparent };
        root.Children.Add(element);

        var window = Stage(request.Dark);
        window.Content = root;
        try
        {
            window.UpdateLayout();

            // Templates, and anything the control does once loaded, before the picture is taken.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(120);
            window.UpdateLayout();

            var bitmap = new RenderTargetBitmap(request.Width * Scale, request.Height * Scale, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            window.Content = null;
        }
    }

    /// <summary>The window controls are drawn in: transparent, off the screen, in the project's theme.</summary>
    private static Window Stage(bool dark)
    {
        if (stage is null)
        {
            stage = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Left = -20000,
                Top = -20000,
                Title = "Standalone UI Builder preview",
            };
            stage.Show();
        }

        if (stageDark != dark)
        {
            stage.Resources.MergedDictionaries.Clear();
            if (dark)
            {
                stage.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml", UriKind.Absolute),
                });
            }

            stageDark = dark;
        }

        return stage;
    }

    /// <summary>Draws a Windows Forms control on a form that is never shown.</summary>
    private static byte[] DrawWinForms(Type type, PreviewRequest request)
    {
        if (!visualStyles)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            visualStyles = true;
        }

        var control = (System.Windows.Forms.Control)Activator.CreateInstance(type)!;
        Apply(control, request.Settings);
        using var form = new System.Windows.Forms.Form
        {
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(-20000, -20000),
            ClientSize = new System.Drawing.Size(request.Width, request.Height),
        };
        control.Bounds = new System.Drawing.Rectangle(0, 0, request.Width, request.Height);
        form.Controls.Add(control);
        _ = form.Handle;
        _ = control.Handle;
        using var bitmap = new System.Drawing.Bitmap(request.Width, request.Height);
        control.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, request.Width, request.Height));
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Sets the design's values, converted to each property's type; a value the control refuses is left out.</summary>
    private static void Apply(object control, IReadOnlyList<LibrarySetting> settings)
    {
        foreach (var setting in settings)
        {
            if (setting.Kind == LibraryValueKind.TypeArgument
                || control.GetType().GetProperty(setting.Name, BindingFlags.Public | BindingFlags.Instance) is not { CanWrite: true } property)
            {
                continue;
            }

            try
            {
                var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                object value = setting.Kind switch
                {
                    LibraryValueKind.Flag => setting.Value == "True",
                    LibraryValueKind.Whole => Convert.ChangeType(long.Parse(setting.Value, CultureInfo.InvariantCulture), target, CultureInfo.InvariantCulture),
                    LibraryValueKind.Number => Convert.ChangeType(double.Parse(setting.Value, CultureInfo.InvariantCulture), target, CultureInfo.InvariantCulture),
                    LibraryValueKind.Choice => Enum.Parse(target, setting.Value),
                    _ => setting.Value,
                };
                property.SetValue(control, value);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException or TargetInvocationException)
            {
                // Drawn without it, as the running app would fail to set it too.
            }
        }
    }

    /// <summary>Registers a vendor's licence key once, before its controls are made, as the exported app does.</summary>
    private static void RegisterLicenses(IReadOnlyDictionary<string, string> keys)
    {
        if (keys.TryGetValue("Syncfusion", out var key) && RegisteredLicenses.Add("Syncfusion") && AssemblyPaths.ContainsKey("Syncfusion.Licensing"))
        {
            var licensing = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Syncfusion.Licensing"));
            licensing.GetType("Syncfusion.Licensing.SyncfusionLicenseProvider")?
                .GetMethod("RegisterLicense", BindingFlags.Public | BindingFlags.Static, [typeof(string)])?
                .Invoke(null, [key]);
        }
    }
}
