using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Libraries;

namespace StandaloneUiBuilder;

/// <summary>
/// Project > Libraries: the project's control libraries. Adding one downloads the package from
/// NuGet (or the folder the UIB_PACKAGE_FEED test hook names) and finds its controls for the
/// project's platform; every change is an ordinary edit, so Undo takes it back.
/// </summary>
public partial class LibrariesWindow : Window
{
    private static readonly HttpClient Http = CreateClient();

    private readonly DesignEditor editor;
    private CancellationTokenSource? download;

    private sealed record LibraryItem(string Id, string Summary);

    private LibrariesWindow(DesignEditor editor)
    {
        InitializeComponent();
        this.editor = editor;
        var platform = editor.Document.Platform.DisplayName();
        IntroText.Text = $"NuGet packages of {platform} controls, such as Syncfusion, Telerik or DevExpress. Their controls appear in the toolbox, "
            + $"grouped by package, and every export references them. A library has controls for one platform, so choose {platform} packages.";
        Refresh();
        Loaded += (_, _) => PackageBox.Focus();
        Closed += (_, _) => download?.Cancel();
    }

    public static void Show(Window owner, DesignEditor editor)
    {
        var window = new LibrariesWindow(editor) { Owner = owner };
        window.ShowDialog();
    }

    /// <summary>Where packages come from: nuget.org, or the folder the test hook names.</summary>
    public static PackageCache CreateCache()
    {
        IPackageFeed feed = Environment.GetEnvironmentVariable("UIB_PACKAGE_FEED") is { Length: > 0 } folder
            ? new FolderFeed(folder)
            : new FlatContainerFeed(Http);
        var cacheFolder = Environment.GetEnvironmentVariable("UIB_PACKAGE_CACHE") is { Length: > 0 } cache ? cache : PackageCache.DefaultFolder;
        return new PackageCache(feed, cacheFolder);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StandaloneUiBuilder/1.0");
        return client;
    }

    private void Refresh()
    {
        var libraries = editor.Document.Libraries ?? [];
        var selected = (LibraryList.SelectedItem as LibraryItem)?.Id;
        LibraryList.ItemsSource = libraries
            .Select(l => new LibraryItem(l.Id, $"{l.Version} · {l.Controls.Count} control{(l.Controls.Count == 1 ? "" : "s")}"))
            .ToList();
        LibraryList.SelectedItem = LibraryList.Items.Cast<LibraryItem>().FirstOrDefault(i => i.Id == selected);
        EmptyText.Visibility = libraries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var licensed = libraries.Any(l => LibraryValues.LicensedVendor(l.Id) == "Syncfusion");
        LicensePanel.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;
        if (!SyncfusionKeyBox.IsKeyboardFocused)
        {
            SyncfusionKeyBox.Text = editor.Document.LicenseKeys?.GetValueOrDefault("Syncfusion") ?? "";
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var busy = download is not null;
        AddButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = UpdateButton.IsEnabled = !busy && LibraryList.SelectedItem is not null;
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LibraryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void PackageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AddButton.IsEnabled)
        {
            Add_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await AddAsync(PackageBox.Text, VersionBox.Text);

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is LibraryItem item)
        {
            await AddAsync(item.Id, "");
        }
    }

    /// <summary>Downloads a package, finds its controls, and adds it to the project (or updates it).</summary>
    private async Task AddAsync(string id, string version)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            LibraryStatusText.Text = "Type a package name, such as Syncfusion.SfGrid.WPF.";
            PackageBox.Focus();
            return;
        }

        download = new CancellationTokenSource();
        UpdateButtons();
        LibraryStatusText.Text = "";
        var progress = new Progress<string>(message => BusyText.Text = message);
        var cache = CreateCache();
        var platform = editor.Document.Platform;
        var token = download.Token;
        try
        {
            var package = await Task.Run(() => LibraryLoader.LoadAsync(cache, id, string.IsNullOrWhiteSpace(version) ? null : version, platform, progress, token), token);
            var existing = editor.Document.Libraries?.FirstOrDefault(l => string.Equals(l.Id, package.Id, StringComparison.OrdinalIgnoreCase));
            if (editor.AddLibrary(package) is { } error)
            {
                LibraryStatusText.Text = error;
            }
            else
            {
                LibraryStatusText.Text = existing is null
                    ? $"Added {package.Id} {package.Version}: {package.Controls.Count} control{(package.Controls.Count == 1 ? "" : "s")} in the toolbox."
                    : $"{package.Id} is now {package.Version}.";
                PackageBox.Clear();
                VersionBox.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            LibraryStatusText.Text = "Stopped.";
        }
        catch (LibraryException ex)
        {
            LibraryStatusText.Text = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            LibraryStatusText.Text = $"Could not download the package: {ex.Message}";
        }
        finally
        {
            download.Dispose();
            download = null;
            Refresh();
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e) => download?.Cancel();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is LibraryItem item)
        {
            LibraryStatusText.Text = editor.RemoveLibrary(item.Id) ?? $"Removed {item.Id}.";
            Refresh();
        }
    }

    private void SyncfusionKeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (editor.SetLicenseKey("Syncfusion", SyncfusionKeyBox.Text))
        {
            LibraryStatusText.Text = SyncfusionKeyBox.Text.Trim().Length > 0 ? "Syncfusion licence key saved in the project." : "Syncfusion licence key removed.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SyncfusionKeyBox_LostKeyboardFocus(sender, null!);
        Close();
    }
}
