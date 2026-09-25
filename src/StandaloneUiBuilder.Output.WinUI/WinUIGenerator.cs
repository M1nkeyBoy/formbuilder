using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.WinUI;

/// <summary>
/// Generates a WinUI 3 desktop app (Windows App SDK, unpackaged and self-contained) from a
/// builder project: a window per screen holding its controls at their designed positions.
/// The first screen is MainWindow, which the app opens; the others are named after themselves
/// (SettingsWindow). Output is deterministic: the same document always produces the same text.
/// </summary>
/// <remarks>
/// Layout follows the WPF output, since WinUI XAML has the same panels, alignment and margins.
/// WinUI has no Label or GroupBox: a Label is a ContentControl, which centres its text the same
/// way, and a GroupBox is a Grid drawing a frame and title around a StackPanel at the fixed
/// inset. WinUI controls have minimum sizes in their default styles, so every control resets
/// them to keep its designed size.
/// </remarks>
public static class WinUIGenerator
{
    /// <summary>The first screen's window, which the app opens.</summary>
    public const string WindowClassName = "MainWindow";

    /// <summary>The Windows App SDK the generated project uses.</summary>
    public const string WindowsAppSdkVersion = "1.8.260921001";

    private const string ClassSuffix = "Window";

    // Names that would clash with members the generated window already has. The window's own
    // class name is checked separately.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "InitializeComponent", "InitializeWindow", "GetDpiForWindow", "Content", "Title", "AppWindow",
        "Activate", "Close", "Closed", "Activated", "Bounds", "Visible", "Dispatcher", "DispatcherQueue",
        "SystemBackdrop", "ExtendsContentIntoTitleBar", "SetTitleBar",
    };

    /// <summary>The one event per control type that gets a hook, and its handler's parameters.</summary>
    private static (string Event, string Sender, string Args)? EventFor(ControlType type) => type switch
    {
        ControlType.Button or ControlType.CheckBox or ControlType.RadioButton => ("Click", "object", "RoutedEventArgs"),
        ControlType.TextBox => ("TextChanged", "object", "TextChangedEventArgs"),
        ControlType.PasswordBox => ("PasswordChanged", "object", "RoutedEventArgs"),
        ControlType.ComboBox or ControlType.ListBox => ("SelectionChanged", "object", "SelectionChangedEventArgs"),
        ControlType.Slider => ("ValueChanged", "object", "RangeBaseValueChangedEventArgs"),
        ControlType.DatePicker => ("DateChanged", "CalendarDatePicker", "CalendarDatePickerDateChangedEventArgs"),
        _ => null,
    };

    public static string HandlerName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"{control.Name}_{e.Event}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    public static string HookName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"On{control.Name}{e.Event}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    /// <summary>The class a screen's window gets: MainWindow for the first screen, otherwise SettingsWindow and so on.</summary>
    public static string ClassName(ProjectDocument document, ScreenDocument screen) =>
        CodeNames.ScreenClassName(document, screen, ClassSuffix);

    /// <summary>Returns reasons the document cannot be exported to WinUI as it stands, or an empty list.</summary>
    public static IReadOnlyList<string> Check(ProjectDocument document)
    {
        var problems = CodeNames.CheckScreenClassNames(document, ClassSuffix, "window").ToList();
        foreach (var screen in document.Screens)
        {
            var prefix = document.Screens.Count > 1 ? $"Screen \"{screen.Name}\": " : "";
            problems.AddRange(CheckScreen(ClassName(document, screen), screen).Select(p => prefix + p));
        }

        return problems;
    }

    private static List<string> CheckScreen(string className, ScreenDocument screen)
    {
        var problems = new List<string>();
        var all = ControlTree.All(screen.Controls).ToList();
        foreach (var control in all)
        {
            if (CodeNames.CSharpKeywords.Contains(control.Name))
            {
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in WinUI code. Rename it.");
            }
            else if (ReservedNames.Contains(control.Name) || control.Name == className)
            {
                problems.Add($"\"{control.Name}\" clashes with a member of the generated window. Rename the control.");
            }
        }

        var generatedMembers = all
            .Where(c => EventFor(c.Type) is not null)
            .SelectMany(c => new[] { (Member: HandlerName(c), Owner: c.Name), (Member: HookName(c), Owner: c.Name) })
            .ToDictionary(m => m.Member, m => m.Owner, StringComparer.Ordinal);
        foreach (var control in all)
        {
            if (generatedMembers.TryGetValue(control.Name, out var owner))
            {
                problems.Add($"\"{control.Name}\" clashes with the generated event code for \"{owner}\". Rename one of them.");
            }
        }

        return problems;
    }

    public static IReadOnlyList<GeneratedFile> Generate(ProjectDocument document, string rootNamespace)
    {
        List<GeneratedFile> files =
        [
            new($"{rootNamespace}.csproj", ProjectFileText(rootNamespace), Regenerate: false),
            new("app.manifest", Manifest(rootNamespace), Regenerate: false),
            new("App.xaml", AppXaml(rootNamespace), Regenerate: false),
            new("App.xaml.cs", AppCode(rootNamespace), Regenerate: false),
        ];

        foreach (var screen in document.Screens)
        {
            var className = ClassName(document, screen);
            files.Add(new($"{className}.xaml", WindowXaml(document, screen, rootNamespace), Regenerate: true));
            files.Add(new($"{className}.xaml.cs", WindowCode(rootNamespace, className), Regenerate: false));
            files.Add(new($"{className}.g.cs", WindowGeneratedCode(document, screen, rootNamespace), Regenerate: true));
        }

        files.AddRange(CodeNames.ImageFiles(document));
        return files;
    }

    /// <summary>The first screen's window.</summary>
    public static string WindowXaml(ProjectDocument document, string rootNamespace) =>
        WindowXaml(document, document.MainScreen, rootNamespace);

    public static string WindowXaml(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        var className = ClassName(document, screen);
        var xaml = new StringBuilder();
        xaml.AppendLine($"<!-- {ProjectExporter.GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        xaml.AppendLine($"     put your own code in {className}.xaml.cs. -->");
        xaml.AppendLine($"<Window x:Class=\"{rootNamespace}.{className}\"");
        xaml.AppendLine("        xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        xaml.AppendLine("        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");

        // The window opens at the design size (see the generated code). If any control follows
        // the right or bottom edge it can be enlarged, and controls move or stretch with their
        // anchors; otherwise it keeps the design size.
        var resizable = AnchorLayout.IsResizable(screen);
        var size = resizable
            ? $"MinWidth=\"{Number(screen.Width)}\" MinHeight=\"{Number(screen.Height)}\""
            : $"Width=\"{Number(screen.Width)}\" Height=\"{Number(screen.Height)}\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\"";
        xaml.AppendLine($"    <Grid {size} Background=\"{{ThemeResource ApplicationPageBackgroundThemeBrush}}\">");
        foreach (var control in screen.Controls)
        {
            AppendElement(xaml, screen, control, RootLayout(screen, control), depth: 2);
        }

        xaml.AppendLine("    </Grid>");
        xaml.AppendLine("</Window>");
        return xaml.ToString();
    }

    /// <summary>A control placed directly on the screen: alignment and margins from its anchors.</summary>
    private static List<string> RootLayout(ScreenDocument screen, ControlDocument control)
    {
        var placement = AnchorLayout.Place(screen, control);
        var margin = new[]
        {
            placement.Horizontal == AxisAlignment.End ? 0 : placement.MarginLeft,
            placement.Vertical == AxisAlignment.End ? 0 : placement.MarginTop,
            placement.Horizontal == AxisAlignment.Start ? 0 : placement.MarginRight,
            placement.Vertical == AxisAlignment.Start ? 0 : placement.MarginBottom,
        };
        var layout = new List<string>
        {
            $"HorizontalAlignment=\"{Alignment(placement.Horizontal, "Left", "Right")}\"",
            $"VerticalAlignment=\"{Alignment(placement.Vertical, "Top", "Bottom")}\"",
            $"Margin=\"{string.Join(",", margin.Select(Number))}\"",
        };
        if (placement.Width is { } width)
        {
            layout.Add($"Width=\"{Number(width)}\"");
        }

        if (placement.Height is { } height)
        {
            layout.Add($"Height=\"{Number(height)}\"");
        }

        return layout;
    }

    /// <summary>
    /// Writes one control with the layout attributes its parent needs, then, for a container,
    /// its children with theirs, as the WPF output does.
    /// </summary>
    private static void AppendElement(StringBuilder xaml, ScreenDocument screen, ControlDocument control, List<string> layout, int depth)
    {
        if (control.Type == ControlType.GroupBox)
        {
            AppendGroupBox(xaml, screen, control, layout, depth);
            return;
        }

        var indent = new string(' ', depth * 4);
        var properties = control.Properties;
        var element = control.Type switch
        {
            ControlType.Label => "ContentControl",
            ControlType.DatePicker => "CalendarDatePicker",
            _ => control.Type.ToString(),
        };
        var attributes = new List<string> { $"x:Name=\"{control.Name}\"" };
        attributes.AddRange(layout);

        // WinUI's default styles give many controls a minimum size; the design's size wins.
        if (!ControlCatalog.Get(control.Type).IsContainer && control.Type != ControlType.Image)
        {
            attributes.Add("MinWidth=\"0\" MinHeight=\"0\"");
        }

        switch (control.Type)
        {
            case ControlType.Label:
                attributes.Add("IsTabStop=\"False\"");
                attributes.Add("Padding=\"2,0\"");
                attributes.Add("HorizontalContentAlignment=\"Left\"");
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"Content=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.Button:
                attributes.Add($"Content=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.TextBox when properties.IsMultiline == true:
                attributes.Add("AcceptsReturn=\"True\"");
                attributes.Add("TextWrapping=\"Wrap\"");
                attributes.Add("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"");
                attributes.Add($"Text=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.TextBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"Text=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.PasswordBox:
            case ControlType.DatePicker:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                break;
            case ControlType.CheckBox:
            case ControlType.RadioButton:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"IsChecked=\"{(properties.IsChecked == true ? "True" : "False")}\"");
                attributes.Add($"Content=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.ComboBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                break;
            case ControlType.ListBox:
                break;
            case ControlType.Slider:
            case ControlType.ProgressBar:
                attributes.Add($"Minimum=\"{Number(properties.Minimum ?? 0)}\"");
                attributes.Add($"Maximum=\"{Number(properties.Maximum ?? 100)}\"");
                attributes.Add($"Value=\"{Number(properties.Value ?? 0)}\"");
                if (control.Type == ControlType.Slider)
                {
                    attributes.Add("StepFrequency=\"1\"");
                }

                break;
            case ControlType.Image:
                if (properties.ImageData is not null)
                {
                    attributes.Add($"Source=\"ms-appx:///{ImageFile.ExportPath(screen, control)}\"");
                }

                attributes.Add($"Stretch=\"{properties.Stretch ?? ImageStretch.Uniform}\"");
                break;
            case ControlType.StackPanel:
                attributes.Add($"Orientation=\"{(properties.Orientation == StackOrientation.Horizontal ? "Horizontal" : "Vertical")}\"");
                break;
            case ControlType.Grid:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type.");
        }

        attributes.AddRange(StyleAttributes(properties));
        if (EventFor(control.Type) is { } hook)
        {
            attributes.Add($"{hook.Event}=\"{HandlerName(control)}\"");
        }

        var opening = $"{indent}<{element} {string.Join(" ", attributes)}";
        var items = control.Type is ControlType.ComboBox or ControlType.ListBox ? properties.Items ?? [] : [];
        var itemElement = control.Type == ControlType.ListBox ? "ListBoxItem" : "ComboBoxItem";
        var children = control.Children ?? [];
        if (items.Count == 0 && children.Count == 0 && control.Type != ControlType.Grid)
        {
            xaml.AppendLine(opening + " />");
            return;
        }

        xaml.AppendLine(opening + ">");
        foreach (var item in items)
        {
            xaml.AppendLine($"{indent}    <{itemElement} Content=\"{Attribute(item)}\" />");
        }

        if (control.Type == ControlType.StackPanel)
        {
            AppendStackChildren(xaml, screen, control, depth + 1);
        }
        else if (control.Type == ControlType.Grid)
        {
            xaml.AppendLine($"{indent}    <Grid.RowDefinitions>");
            foreach (var size in GridTrackSize.Resolve(properties.RowSizes, properties.Rows ?? 1))
            {
                xaml.AppendLine($"{indent}        <RowDefinition Height=\"{size}\" />");
            }

            xaml.AppendLine($"{indent}    </Grid.RowDefinitions>");
            xaml.AppendLine($"{indent}    <Grid.ColumnDefinitions>");
            foreach (var size in GridTrackSize.Resolve(properties.ColumnSizes, properties.Columns ?? 1))
            {
                xaml.AppendLine($"{indent}        <ColumnDefinition Width=\"{size}\" />");
            }

            xaml.AppendLine($"{indent}    </Grid.ColumnDefinitions>");
            foreach (var child in children)
            {
                var cell = new List<string>
                {
                    $"Grid.Row=\"{Number(child.Row ?? 0)}\"",
                    $"Grid.Column=\"{Number(child.Column ?? 0)}\"",
                };
                if (child.RowSpan is > 1)
                {
                    cell.Add($"Grid.RowSpan=\"{Number(child.RowSpan.Value)}\"");
                }

                if (child.ColumnSpan is > 1)
                {
                    cell.Add($"Grid.ColumnSpan=\"{Number(child.ColumnSpan.Value)}\"");
                }

                cell.Add("HorizontalAlignment=\"Stretch\"");
                cell.Add("VerticalAlignment=\"Stretch\"");
                AppendElement(xaml, screen, child, cell, depth + 1);
            }
        }

        xaml.AppendLine($"{indent}</{element}>");
    }

    /// <summary>
    /// A StackPanel's children: each keeps its size along the stack, stretches across it and
    /// has the spacing as a leading margin.
    /// </summary>
    private static void AppendStackChildren(StringBuilder xaml, ScreenDocument screen, ControlDocument stack, int depth)
    {
        var properties = stack.Properties;
        var children = stack.Children ?? [];
        var vertical = properties.Orientation != StackOrientation.Horizontal;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var gap = i > 0 ? properties.Spacing ?? 0 : 0;
            var childLayout = vertical
                ? new List<string> { "HorizontalAlignment=\"Stretch\"", $"Height=\"{Number(child.Height)}\"" }
                : new List<string> { "VerticalAlignment=\"Stretch\"", $"Width=\"{Number(child.Width)}\"" };
            if (gap > 0)
            {
                childLayout.Add($"Margin=\"{(vertical ? $"0,{Number(gap)},0,0" : $"{Number(gap)},0,0,0")}\"");
            }

            AppendElement(xaml, screen, child, childLayout, depth);
        }
    }

    /// <summary>
    /// A GroupBox, which WinUI does not have: a named Grid in its place holding a rounded frame,
    /// the title over the frame's top edge, and a StackPanel at the fixed inset for the children.
    /// </summary>
    private static void AppendGroupBox(StringBuilder xaml, ScreenDocument screen, ControlDocument group, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var properties = group.Properties;
        var (left, top, right, bottom) = ContainerLayout.GroupBoxInset;
        xaml.AppendLine($"{indent}<Grid x:Name=\"{group.Name}\" {string.Join(" ", layout)}>");
        var frameFill = properties.Background is { } fill ? $" Background=\"{fill}\"" : "";
        xaml.AppendLine($"{indent}    <Border Margin=\"0,8,0,0\" BorderBrush=\"{{ThemeResource CardStrokeColorDefaultBrush}}\" BorderThickness=\"1\" CornerRadius=\"4\"{frameFill} />");
        var titleStyle = string.Concat(StyleAttributes(properties with { Background = null }).Select(a => " " + a));
        xaml.AppendLine($"{indent}    <Border Margin=\"6,0,0,0\" Padding=\"3,0\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Background=\"{{ThemeResource ApplicationPageBackgroundThemeBrush}}\">");
        xaml.AppendLine($"{indent}        <TextBlock Text=\"{Attribute(properties.Text ?? "")}\"{titleStyle} />");
        xaml.AppendLine($"{indent}    </Border>");
        var orientation = properties.Orientation == StackOrientation.Horizontal ? "Horizontal" : "Vertical";
        var margin = string.Join(",", new[] { left, top, right, bottom }.Select(Number));
        var opening = $"{indent}    <StackPanel Margin=\"{margin}\" Orientation=\"{orientation}\"";
        if (group.Children is not { Count: > 0 })
        {
            xaml.AppendLine(opening + " />");
        }
        else
        {
            xaml.AppendLine(opening + ">");
            AppendStackChildren(xaml, screen, group, depth + 2);
            xaml.AppendLine($"{indent}    </StackPanel>");
        }

        xaml.AppendLine($"{indent}</Grid>");
    }

    /// <summary>A control's own text size, weight and colours, where the design sets them.</summary>
    private static IEnumerable<string> StyleAttributes(ControlProperties properties)
    {
        if (properties.FontSize is { } size)
        {
            yield return $"FontSize=\"{Number(size)}\"";
        }

        if (properties.IsBold == true)
        {
            yield return "FontWeight=\"Bold\"";
        }

        if (properties.Foreground is { } text)
        {
            yield return $"Foreground=\"{text}\"";
        }

        if (properties.Background is { } fill)
        {
            yield return $"Background=\"{fill}\"";
        }
    }

    /// <summary>
    /// The regenerated code behind a window: its title and design size, the handlers the XAML
    /// refers to, and a partial method (hook) per control the developer may implement.
    /// </summary>
    public static string WindowGeneratedCode(ProjectDocument document, string rootNamespace) =>
        WindowGeneratedCode(document, document.MainScreen, rootNamespace);

    public static string WindowGeneratedCode(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        var className = ClassName(document, screen);
        var title = screen.Id == document.MainScreen.Id ? document.Name : screen.Name;
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {className}.xaml.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClick(RoutedEventArgs e) => Title = \"Submitted\";");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine("using Microsoft.UI.Windowing;");
        code.AppendLine("using Microsoft.UI.Xaml;");
        code.AppendLine("using Microsoft.UI.Xaml.Controls;");
        code.AppendLine("using Microsoft.UI.Xaml.Controls.Primitives;");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"public partial class {className}");
        code.AppendLine("{");
        code.AppendLine("    /// <summary>Sets the title and opens the window at the design size. Called by the constructor.</summary>");
        code.AppendLine("    private void InitializeWindow()");
        code.AppendLine("    {");
        code.AppendLine($"        Title = {Literal(title)};");
        code.AppendLine();
        code.AppendLine("        // The design is in DIPs; the window is sized in pixels at this display's scaling.");
        code.AppendLine("        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;");
        code.AppendLine($"        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round({Number(screen.Width)} * scale), (int)Math.Round({Number(screen.Height)} * scale)));");
        if (!AnchorLayout.IsResizable(screen))
        {
            code.AppendLine();
            code.AppendLine("        // No control follows the right or bottom edge, so the window keeps the design size.");
            code.AppendLine("        if (AppWindow.Presenter is OverlappedPresenter presenter)");
            code.AppendLine("        {");
            code.AppendLine("            presenter.IsResizable = false;");
            code.AppendLine("            presenter.IsMaximizable = false;");
            code.AppendLine("        }");
        }

        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    [System.Runtime.InteropServices.DllImport(\"user32.dll\")]");
        code.AppendLine("    private static extern uint GetDpiForWindow(nint hwnd);");

        var hooked = ControlTree.All(screen.Controls).Where(c => EventFor(c.Type) is not null).ToList();
        foreach (var control in hooked)
        {
            var e = EventFor(control.Type)!.Value;
            code.AppendLine();
            var action = control.Properties.OpensScreen is { } id && document.FindScreen(id) is { } target
                ? $"new {ClassName(document, target)}().Activate();"
                : control.Properties.ClosesScreen == true ? "Close();"
                : null;
            if (action is null)
            {
                code.AppendLine($"    private void {HandlerName(control)}({e.Sender} sender, {e.Args} e) => {HookName(control)}(e);");
            }
            else
            {
                // The hook runs first, then the button's action from the design.
                code.AppendLine($"    private void {HandlerName(control)}({e.Sender} sender, {e.Args} e)");
                code.AppendLine("    {");
                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine($"        {action}");
                code.AppendLine("    }");
            }
        }

        foreach (var control in hooked)
        {
            var e = EventFor(control.Type)!.Value;
            code.AppendLine();
            code.AppendLine($"    /// <summary>{control.Type} \"{control.Name}\": {e.Event}.</summary>");
            code.AppendLine($"    partial void {HookName(control)}({e.Args} e);");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    private static string WindowCode(string rootNamespace, string className) => $$"""
        using Microsoft.UI.Xaml;

        namespace {{rootNamespace}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The controls are declared in {{className}}.xaml, and the window's size, event wiring and
        // hooks are in {{className}}.g.cs; both are regenerated on every export. To respond to a
        // control, implement its hook, for example:
        //     partial void OnSubmitButtonClick(RoutedEventArgs e) => Title = "Submitted";
        public sealed partial class {{className}} : Window
        {
            public {{className}}()
            {
                InitializeComponent();
                InitializeWindow();
            }
        }

        """;

    private static string AppXaml(string rootNamespace) => $$"""
        <Application x:Class="{{rootNamespace}}.App"
                     xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Application.Resources>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
            </Application.Resources>
        </Application>

        """;

    private static string AppCode(string rootNamespace) => $$"""
        using Microsoft.UI.Xaml;

        namespace {{rootNamespace}};

        public partial class App : Application
        {
            private Window? window;

            public App()
            {
                InitializeComponent();
            }

            protected override void OnLaunched(LaunchActivatedEventArgs args)
            {
                window = new {{WindowClassName}}();
                window.Activate();
            }
        }

        """;

    private static string Manifest(string rootNamespace) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
          <assemblyIdentity version="1.0.0.0" name="{{rootNamespace}}.app" />
          <application xmlns="urn:schemas-microsoft-com:asm.v3">
            <windowsSettings>
              <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
            </windowsSettings>
          </application>
        </assembly>

        """;

    /// <summary>
    /// An unpackaged, self-contained WinUI 3 app: it runs from its build folder like any other
    /// program, with no installer and no separately installed Windows App SDK runtime.
    /// </summary>
    private static string ProjectFileText(string rootNamespace) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
            <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
            <RootNamespace>{{rootNamespace}}</RootNamespace>
            <ApplicationManifest>app.manifest</ApplicationManifest>
            <Platforms>x86;x64;ARM64</Platforms>
            <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
            <!-- Builds for this computer's processor unless another is chosen. -->
            <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())</RuntimeIdentifier>
            <UseWinUI>true</UseWinUI>
            <WindowsPackageType>None</WindowsPackageType>
            <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>

          <ItemGroup>
            <!-- Pictures from the design, copied next to the application (ms-appx:///Assets/...). -->
            <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.WindowsAppSDK" Version="{{WindowsAppSdkVersion}}" />
            <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.9169" />
          </ItemGroup>

        </Project>

        """;

    private static string Alignment(AxisAlignment alignment, string start, string end) => alignment switch
    {
        AxisAlignment.Start => start,
        AxisAlignment.End => end,
        _ => "Stretch",
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Literal(string value)
    {
        var escaped = new StringBuilder("\"");
        foreach (var c in value)
        {
            escaped.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) || c is (char)0x2028 or (char)0x2029 => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return escaped.Append('"').ToString();
    }

    private static string Attribute(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            escaped.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\n' => "&#10;",
                '\r' => "&#13;",
                '\t' => "&#9;",

                // A leading "{" would be read as a markup extension.
                '{' when escaped.Length == 0 => "{}{",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    // "--" is not allowed inside an XML comment.
    private static string Comment(string value) => value.Replace("--", "- -", StringComparison.Ordinal);
}
