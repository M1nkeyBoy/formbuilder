using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Maui;

/// <summary>
/// Generates a .NET MAUI app from a builder project: a ContentPage per screen holding its
/// controls at their designed positions. The first screen is MainPage, which the app opens in
/// its window; the others are named after themselves (SettingsPage). The project targets
/// Windows, where it is built and checked; other platforms can be added to its target list.
/// Output is deterministic: the same document always produces the same text.
/// </summary>
/// <remarks>
/// Layout follows the WPF output: a root Grid whose children have alignment (layout options)
/// and margins from their anchors, stack layouts and Grids for containers. MAUI names some
/// controls differently (Entry, Editor, Picker, CollectionView) and has no GroupBox or
/// TabControl, which are drawn as frames holding stacks at their fixed insets; a CheckBox
/// has no text of its own, so it sits beside a Label in a small Grid that takes its place.
/// </remarks>
public static class MauiGenerator
{
    public const string MainPageClassName = "MainPage";

    private const string ClassSuffix = "Page";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "InitializeComponent", "Content", "Title", "Window", "Navigation", "Padding", "Handler", "Parent",
        "BindingContext", "Resources", "Width", "Height", "IsVisible", "Opacity", "Background", "Dispatcher",
    };

    /// <summary>The one event per control type that gets a hook, and its argument type.</summary>
    private static (string Event, string Args)? EventFor(ControlType type) => type switch
    {
        ControlType.Button => ("Clicked", "EventArgs"),
        ControlType.CheckBox or ControlType.RadioButton => ("CheckedChanged", "CheckedChangedEventArgs"),
        ControlType.TextBox or ControlType.PasswordBox => ("TextChanged", "TextChangedEventArgs"),
        ControlType.ComboBox => ("SelectedIndexChanged", "EventArgs"),
        ControlType.ListBox => ("SelectionChanged", "SelectionChangedEventArgs"),
        ControlType.Slider => ("ValueChanged", "ValueChangedEventArgs"),
        ControlType.DatePicker => ("DateSelected", "DateChangedEventArgs"),
        ControlType.TabControl => ("SelectionChanged", "EventArgs"),
        _ => null,
    };

    public static string HandlerName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"{control.Name}_{e.Event}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    public static string HookName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"On{control.Name}{e.Event}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    public static string ClassName(ProjectDocument document, ScreenDocument screen) =>
        CodeNames.ScreenClassName(document, screen, ClassSuffix);

    /// <summary>
    /// The name an Image's picture has in the app. MAUI image names must be lower case letters,
    /// digits and underscores; pictures are converted to PNG, so they are referred to as .png.
    /// </summary>
    public static string ImageName(ScreenDocument screen, ControlDocument image) =>
        $"{screen.Name}_{image.Name}".ToLowerInvariant();

    public static IReadOnlyList<string> Check(ProjectDocument document)
    {
        var problems = CodeNames.CheckScreenClassNames(document, ClassSuffix, "page").ToList();
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
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in MAUI code. Rename it.");
            }
            else if (ReservedNames.Contains(control.Name) || control.Name == className)
            {
                problems.Add($"\"{control.Name}\" clashes with a member of the generated page. Rename the control.");
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
            new($"{rootNamespace}.csproj", ProjectFileText(document, rootNamespace), Regenerate: false),
            new("MauiProgram.cs", ProgramCode(rootNamespace), Regenerate: false),
            new("App.xaml", AppXaml(rootNamespace), Regenerate: false),
            new("App.xaml.cs", AppCode(rootNamespace), Regenerate: false),
            new("App.g.cs", AppGeneratedCode(document, rootNamespace), Regenerate: true),
            new("Platforms/Windows/App.xaml", WindowsAppXaml(rootNamespace), Regenerate: false),
            new("Platforms/Windows/App.xaml.cs", WindowsAppCode(rootNamespace), Regenerate: false),
            new("Platforms/Windows/app.manifest", WindowsManifest(rootNamespace), Regenerate: false),
            new("Platforms/Windows/Package.appxmanifest", PackageManifest(document), Regenerate: false),
        ];

        foreach (var screen in document.Screens)
        {
            var className = ClassName(document, screen);
            files.Add(new($"{className}.xaml", PageXaml(document, screen, rootNamespace), Regenerate: true));
            files.Add(new($"{className}.xaml.cs", PageCode(rootNamespace, className), Regenerate: false));
            files.Add(new($"{className}.g.cs", PageGeneratedCode(document, screen, rootNamespace), Regenerate: true));
            foreach (var image in ControlTree.All(screen.Controls))
            {
                if (image.Properties.ImageData is { } data && ImageFile.TryDecode(data, out var bytes))
                {
                    files.Add(GeneratedFile.Binary($"Resources/Images/{ImageName(screen, image)}{ImageFile.Extension(bytes)}", bytes));
                }
            }
        }

        return files;
    }

    public static string PageXaml(ProjectDocument document, string rootNamespace) => PageXaml(document, document.MainScreen, rootNamespace);

    public static string PageXaml(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        // Text on the design's own backgrounds stays readable in a dark theme.
        screen = ThemeContrast.Apply(document.Theme, screen);
        var className = ClassName(document, screen);
        var title = screen.Id == document.MainScreen.Id ? document.Name : screen.Name;
        var xaml = new StringBuilder();
        xaml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
        xaml.AppendLine($"<!-- {ProjectExporter.GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        xaml.AppendLine($"     put your own code in {className}.xaml.cs. -->");
        xaml.AppendLine("<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"");
        xaml.AppendLine("             xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"");
        xaml.AppendLine($"             x:Class=\"{rootNamespace}.{className}\"");
        xaml.AppendLine($"             Title=\"{Attribute(title)}\">");

        // At least the design size; a screen with right or bottom anchors fills a larger window.
        var size = AnchorLayout.IsResizable(screen)
            ? $"MinimumWidthRequest=\"{Number(screen.Width)}\" MinimumHeightRequest=\"{Number(screen.Height)}\""
            : $"WidthRequest=\"{Number(screen.Width)}\" HeightRequest=\"{Number(screen.Height)}\" HorizontalOptions=\"Start\" VerticalOptions=\"Start\"";
        xaml.AppendLine($"    <Grid {size}>");
        foreach (var control in screen.Controls)
        {
            AppendElement(xaml, screen, control, RootLayout(screen, control), depth: 2);
        }

        xaml.AppendLine("    </Grid>");
        xaml.AppendLine("</ContentPage>");
        return xaml.ToString();
    }

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
            $"HorizontalOptions=\"{Options(placement.Horizontal)}\"",
            $"VerticalOptions=\"{Options(placement.Vertical)}\"",
            $"Margin=\"{string.Join(",", margin.Select(Number))}\"",
        };
        if (placement.Width is { } width)
        {
            layout.Add($"WidthRequest=\"{Number(width)}\"");
        }

        if (placement.Height is { } height)
        {
            layout.Add($"HeightRequest=\"{Number(height)}\"");
        }

        return layout;
    }

    private static void AppendElement(StringBuilder xaml, ScreenDocument screen, ControlDocument control, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var properties = control.Properties;
        switch (control.Type)
        {
            case ControlType.GroupBox:
                AppendGroupBox(xaml, screen, control, layout, depth);
                return;
            case ControlType.TabControl:
                AppendTabControl(xaml, screen, control, layout, depth);
                return;
            case ControlType.Image:
                // Given only a size request, MAUI shows a picture at its natural size in a corner
                // of the box; in a Grid with the designed box, it fills it and fits the picture.
                var source = properties.ImageData is not null ? $" Source=\"{ImageName(screen, control)}.png\"" : "";
                var aspect = properties.Stretch == ImageStretch.Fill ? "Fill" : "AspectFit";
                xaml.AppendLine($"{indent}<Grid {string.Join(" ", layout)}>");
                xaml.AppendLine($"{indent}    <Image x:Name=\"{control.Name}\" AutomationId=\"{control.Name}\"{source} Aspect=\"{aspect}\" HorizontalOptions=\"Fill\" VerticalOptions=\"Fill\" />");
                xaml.AppendLine($"{indent}</Grid>");
                return;
            case ControlType.CheckBox:
                // A CheckBox has no text in MAUI: a Grid in its place holds it and a Label. The
                // box keeps its natural height, centred (squeezed smaller, MAUI does not draw it),
                // and is 32 wide rather than the 120 Windows gives it, so the text follows it.
                xaml.AppendLine($"{indent}<Grid {string.Join(" ", layout)} ColumnDefinitions=\"Auto,*\" ColumnSpacing=\"4\">");
                xaml.AppendLine($"{indent}    <CheckBox x:Name=\"{control.Name}\" AutomationId=\"{control.Name}\" IsChecked=\"{Bool(properties.IsChecked)}\" WidthRequest=\"32\" MinimumWidthRequest=\"0\" VerticalOptions=\"Center\" CheckedChanged=\"{HandlerName(control)}\" />");
                xaml.AppendLine($"{indent}    <Label Grid.Column=\"1\" Text=\"{Attribute(properties.Text ?? "")}\" VerticalTextAlignment=\"Center\" LineBreakMode=\"NoWrap\"{string.Concat(StyleAttributes(control).Select(a => " " + a))} />");
                xaml.AppendLine($"{indent}</Grid>");
                return;
        }

        var element = control.Type switch
        {
            ControlType.TextBox when properties.IsMultiline == true => "Editor",
            ControlType.TextBox or ControlType.PasswordBox => "Entry",
            ControlType.ComboBox => "Picker",
            ControlType.ListBox => "CollectionView",
            ControlType.StackPanel => properties.Orientation == StackOrientation.Horizontal ? "HorizontalStackLayout" : "VerticalStackLayout",
            _ => control.Type.ToString(),
        };
        // MAUI takes UI Automation IDs from AutomationId, not x:Name; both are the control's name.
        var attributes = new List<string> { $"x:Name=\"{control.Name}\" AutomationId=\"{control.Name}\"" };
        attributes.AddRange(layout);
        switch (control.Type)
        {
            case ControlType.Label:
                attributes.Add("Padding=\"2,0\"");
                attributes.Add("VerticalTextAlignment=\"Center\"");
                attributes.Add($"Text=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.Button:
                attributes.Add("Padding=\"0\"");
                attributes.Add($"Text=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.TextBox:
                attributes.Add($"Text=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.PasswordBox:
                attributes.Add("IsPassword=\"True\"");
                break;
            case ControlType.RadioButton:
                attributes.Add($"IsChecked=\"{Bool(properties.IsChecked)}\"");
                attributes.Add($"Content=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.ListBox:
                attributes.Add("SelectionMode=\"Single\"");
                break;
            case ControlType.Slider:
                // MAUI refuses a minimum at or above the current maximum, so the order matters.
                var (minimum, maximum) = (properties.Minimum ?? 0, properties.Maximum ?? 100);
                if (maximum > 0)
                {
                    attributes.Add($"Maximum=\"{Number(maximum)}\" Minimum=\"{Number(minimum)}\"");
                }
                else
                {
                    attributes.Add($"Minimum=\"{Number(minimum)}\" Maximum=\"{Number(maximum)}\"");
                }

                attributes.Add($"Value=\"{Number(properties.Value ?? 0)}\"");
                break;
            case ControlType.ProgressBar:
                // Progress runs from 0 to 1.
                var span = Math.Max(1, (properties.Maximum ?? 100) - (properties.Minimum ?? 0));
                var progress = ((properties.Value ?? 0) - (properties.Minimum ?? 0)) / (double)span;
                attributes.Add($"Progress=\"{progress.ToString("0.####", CultureInfo.InvariantCulture)}\"");
                break;
            case ControlType.StackPanel:
                attributes.Add($"Spacing=\"{Number(properties.Spacing ?? 0)}\"");
                break;
            case ControlType.Grid:
                attributes.Add($"RowDefinitions=\"{string.Join(",", GridTrackSize.Resolve(properties.RowSizes, properties.Rows ?? 1))}\"");
                attributes.Add($"ColumnDefinitions=\"{string.Join(",", GridTrackSize.Resolve(properties.ColumnSizes, properties.Columns ?? 1))}\"");
                break;
        }

        // MAUI controls have their own minimum sizes on some platforms; the design's size wins.
        // Not for a RadioButton, which, like a CheckBox, is not drawn squeezed below its own.
        if (!ControlCatalog.Get(control.Type).IsContainer && control.Type != ControlType.RadioButton)
        {
            attributes.Add("MinimumWidthRequest=\"0\" MinimumHeightRequest=\"0\"");
        }

        attributes.AddRange(StyleAttributes(control));
        if (EventFor(control.Type) is { } hook)
        {
            attributes.Add($"{hook.Event}=\"{HandlerName(control)}\"");
        }

        var opening = $"{indent}<{element} {string.Join(" ", attributes)}";
        var items = control.Type is ControlType.ComboBox or ControlType.ListBox ? properties.Items ?? [] : [];
        var children = control.Children ?? [];
        if (items.Count == 0 && children.Count == 0)
        {
            xaml.AppendLine(opening + " />");
            return;
        }

        xaml.AppendLine(opening + ">");
        if (items.Count > 0)
        {
            xaml.AppendLine($"{indent}    <{element}.ItemsSource>");
            xaml.AppendLine($"{indent}        <x:Array Type=\"{{x:Type x:String}}\">");
            foreach (var item in items)
            {
                xaml.AppendLine($"{indent}            <x:String>{Text(item)}</x:String>");
            }

            xaml.AppendLine($"{indent}        </x:Array>");
            xaml.AppendLine($"{indent}    </{element}.ItemsSource>");
        }

        if (control.Type == ControlType.StackPanel)
        {
            AppendStackChildren(xaml, screen, control, depth + 1);
        }
        else if (control.Type == ControlType.Grid)
        {
            foreach (var child in children)
            {
                var cell = new List<string> { $"Grid.Row=\"{Number(child.Row ?? 0)}\"", $"Grid.Column=\"{Number(child.Column ?? 0)}\"" };
                if (child.RowSpan is > 1)
                {
                    cell.Add($"Grid.RowSpan=\"{Number(child.RowSpan.Value)}\"");
                }

                if (child.ColumnSpan is > 1)
                {
                    cell.Add($"Grid.ColumnSpan=\"{Number(child.ColumnSpan.Value)}\"");
                }

                cell.Add("HorizontalOptions=\"Fill\" VerticalOptions=\"Fill\"");
                AppendElement(xaml, screen, child, cell, depth + 1);
            }
        }

        xaml.AppendLine($"{indent}</{element}>");
    }

    /// <summary>
    /// A stack's children: each keeps its size along the stack and fills across it. The gap is
    /// the stack layout's Spacing.
    /// </summary>
    private static void AppendStackChildren(StringBuilder xaml, ScreenDocument screen, ControlDocument stack, int depth)
    {
        var vertical = stack.Properties.Orientation != StackOrientation.Horizontal;
        foreach (var child in stack.Children ?? [])
        {
            var layout = vertical
                ? new List<string> { "HorizontalOptions=\"Fill\"", $"HeightRequest=\"{Number(child.Height)}\"" }
                : new List<string> { "VerticalOptions=\"Fill\"", $"WidthRequest=\"{Number(child.Width)}\"" };
            AppendElement(xaml, screen, child, layout, depth);
        }
    }

    /// <summary>A GroupBox, which MAUI does not have: a frame, its title, and a stack at the fixed inset.</summary>
    private static void AppendGroupBox(StringBuilder xaml, ScreenDocument screen, ControlDocument group, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var properties = group.Properties;
        var (left, top, right, bottom) = ContainerLayout.GroupBoxInset;
        xaml.AppendLine($"{indent}<Grid x:Name=\"{group.Name}\" {string.Join(" ", layout)}>");
        var fill = properties.Background is { } background ? $" BackgroundColor=\"{background}\"" : "";
        xaml.AppendLine($"{indent}    <Border Margin=\"0,8,0,0\" Stroke=\"{ThemeBinding(LineColor)}\" StrokeThickness=\"1\" StrokeShape=\"RoundRectangle 4\"{fill} />");
        var titleStyle = string.Concat(StyleAttributes(group, background: false).Select(a => " " + a));
        xaml.AppendLine($"{indent}    <Label Text=\"{Attribute(properties.Text ?? "")}\" Margin=\"6,0,0,0\" Padding=\"3,0\" HorizontalOptions=\"Start\" VerticalOptions=\"Start\" BackgroundColor=\"{{AppThemeBinding Light=White, Dark=Black}}\"{titleStyle} />");
        var stack = properties.Orientation == StackOrientation.Horizontal ? "HorizontalStackLayout" : "VerticalStackLayout";
        var margin = string.Join(",", new[] { left, top, right, bottom }.Select(Number));
        var opening = $"{indent}    <{stack} Margin=\"{margin}\" Spacing=\"{Number(properties.Spacing ?? 0)}\"";
        if (group.Children is not { Count: > 0 })
        {
            xaml.AppendLine(opening + " />");
        }
        else
        {
            xaml.AppendLine(opening + ">");
            AppendStackChildren(xaml, screen, group, depth + 2);
            xaml.AppendLine($"{indent}    </{stack}>");
        }

        xaml.AppendLine($"{indent}</Grid>");
    }

    /// <summary>The colours of a chosen tab, of the others, and of frames, in the light and dark themes.</summary>
    private static readonly (string Light, string Dark) SelectedTabColor = ("#FFFFFF", "#2B2B2B");
    private static readonly (string Light, string Dark) TabColor = ("#F0F0F0", "#1C1C1C");
    private static readonly (string Light, string Dark) LineColor = ("#D5DFE5", "#4A4A4A");

    private static string ThemeBinding((string Light, string Dark) color) => $"{{AppThemeBinding Light={color.Light}, Dark={color.Dark}}}";

    /// <summary>
    /// A TabControl, which MAUI does not have within a page: a frame under a row of buttons,
    /// one per tab, and each page as a stack at the fixed <see cref="ContainerLayout.TabControlInset"/>.
    /// Clicking a tab runs the generated handler, which shows its page.
    /// </summary>
    private static void AppendTabControl(StringBuilder xaml, ScreenDocument screen, ControlDocument tabs, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var pages = tabs.Children ?? [];
        var shown = ContainerLayout.ShownTab(tabs);
        var (left, top, right, bottom) = ContainerLayout.TabControlInset;
        xaml.AppendLine($"{indent}<Grid x:Name=\"{tabs.Name}\" {string.Join(" ", layout)}>");
        xaml.AppendLine($"{indent}    <Border Margin=\"0,28,0,0\" Stroke=\"{ThemeBinding(LineColor)}\" StrokeThickness=\"1\" StrokeShape=\"RoundRectangle 4\" />");
        if (pages.Count > 0)
        {
            xaml.AppendLine($"{indent}    <HorizontalStackLayout Spacing=\"2\" VerticalOptions=\"Start\" HeightRequest=\"28\">");
            for (var i = 0; i < pages.Count; i++)
            {
                var color = ThemeBinding(i == shown ? SelectedTabColor : TabColor);
                xaml.AppendLine($"{indent}        <Button Text=\"{Attribute(pages[i].Properties.Text ?? "")}\" HeightRequest=\"28\" MinimumHeightRequest=\"0\" MinimumWidthRequest=\"0\" Padding=\"10,0\" CornerRadius=\"3\" BorderColor=\"{ThemeBinding(LineColor)}\" BorderWidth=\"1\" TextColor=\"{{AppThemeBinding Light=Black, Dark=White}}\" BackgroundColor=\"{color}\" Clicked=\"{HandlerName(tabs)}\" />");
            }

            xaml.AppendLine($"{indent}    </HorizontalStackLayout>");
        }

        var margin = string.Join(",", new[] { left, top, right, bottom }.Select(Number));
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var properties = page.Properties;
            var stack = properties.Orientation == StackOrientation.Horizontal ? "HorizontalStackLayout" : "VerticalStackLayout";
            var visible = i == shown ? "" : " IsVisible=\"False\"";
            var style = string.Concat(StyleAttributes(page).Select(a => " " + a));
            var opening = $"{indent}    <{stack} x:Name=\"{page.Name}\" Margin=\"{margin}\" Spacing=\"{Number(properties.Spacing ?? 0)}\"{visible}{style}";
            if (page.Children is not { Count: > 0 })
            {
                xaml.AppendLine(opening + " />");
                continue;
            }

            xaml.AppendLine(opening + ">");
            AppendStackChildren(xaml, screen, page, depth + 2);
            xaml.AppendLine($"{indent}    </{stack}>");
        }

        xaml.AppendLine($"{indent}</Grid>");
    }

    private static IEnumerable<string> StyleAttributes(ControlDocument control, bool background = true)
    {
        var properties = control.Properties;

        // A CollectionView (ListBox) has no font or text colour of its own in MAUI.
        var text = control.Type != ControlType.ListBox;
        if (text && properties.FontSize is { } size)
        {
            yield return $"FontSize=\"{Number(size)}\"";
        }

        if (text && properties.IsBold == true)
        {
            yield return "FontAttributes=\"Bold\"";
        }

        if (text && properties.Foreground is { } color)
        {
            yield return $"TextColor=\"{color}\"";
        }

        if (background && properties.Background is { } fill)
        {
            yield return $"BackgroundColor=\"{fill}\"";
        }
    }

    public static string PageGeneratedCode(ProjectDocument document, string rootNamespace) =>
        PageGeneratedCode(document, document.MainScreen, rootNamespace);

    public static string PageGeneratedCode(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        var className = ClassName(document, screen);
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {className}.xaml.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClicked(EventArgs e) => Title = \"Submitted\";");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"public partial class {className}");
        code.AppendLine("{");

        var hooked = ControlTree.All(screen.Controls).Where(c => EventFor(c.Type) is not null).ToList();
        var first = true;
        foreach (var control in hooked)
        {
            var e = EventFor(control.Type)!.Value;
            if (!first)
            {
                code.AppendLine();
            }

            first = false;
            var action = control.Properties.OpensScreen is { } id && document.FindScreen(id) is { } target
                ? $"await Navigation.PushModalAsync(new {ClassName(document, target)}());"
                : control.Properties.ClosesScreen == true ? "await CloseAsync();"
                : null;
            if (control.Type == ControlType.TabControl)
            {
                // The clicked tab is drawn as chosen, and its page is the only one shown.
                code.AppendLine($"    private void {HandlerName(control)}(object? sender, {e.Args} e)");
                code.AppendLine("    {");
                code.AppendLine("        var tabs = (Layout)((Element)sender!).Parent;");
                code.AppendLine("        var index = tabs.Children.IndexOf((IView)sender!);");
                code.AppendLine("        for (var i = 0; i < tabs.Children.Count; i++)");
                code.AppendLine("        {");
                code.AppendLine($"            var (light, dark) = i == index ? (\"{SelectedTabColor.Light}\", \"{SelectedTabColor.Dark}\") : (\"{TabColor.Light}\", \"{TabColor.Dark}\");");
                code.AppendLine("            ((Button)tabs.Children[i]).SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb(light), Color.FromArgb(dark));");
                code.AppendLine("        }");
                code.AppendLine();
                var pages = control.Children ?? [];
                for (var i = 0; i < pages.Count; i++)
                {
                    code.AppendLine($"        {pages[i].Name}.IsVisible = index == {Number(i)};");
                }

                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine("    }");
            }
            else if (action is null)
            {
                code.AppendLine($"    private void {HandlerName(control)}(object? sender, {e.Args} e) => {HookName(control)}(e);");
            }
            else
            {
                // The hook runs first, then the button's action from the design.
                code.AppendLine($"    private async void {HandlerName(control)}(object? sender, {e.Args} e)");
                code.AppendLine("    {");
                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine($"        {action}");
                code.AppendLine("    }");
            }
        }

        if (hooked.Any(c => c.Properties.ClosesScreen == true))
        {
            code.AppendLine();
            code.AppendLine("    /// <summary>Closes this screen: back to the screen that opened it, or the window if it is the first.</summary>");
            code.AppendLine("    private async Task CloseAsync()");
            code.AppendLine("    {");
            code.AppendLine("        if (Navigation.ModalStack.Contains(this))");
            code.AppendLine("        {");
            code.AppendLine("            await Navigation.PopModalAsync();");
            code.AppendLine("        }");
            code.AppendLine("        else if (Window is { } window)");
            code.AppendLine("        {");
            code.AppendLine("            Application.Current?.CloseWindow(window);");
            code.AppendLine("        }");
            code.AppendLine("    }");
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

    private static string PageCode(string rootNamespace, string className) => $$"""
        namespace {{rootNamespace}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The controls are declared in {{className}}.xaml and the event wiring is in {{className}}.g.cs;
        // both are regenerated on every export. To respond to a control, implement its hook:
        //     partial void OnSubmitButtonClicked(EventArgs e) => Title = "Submitted";
        public partial class {{className}} : ContentPage
        {
            public {{className}}()
            {
                InitializeComponent();
            }
        }

        """;

    /// <summary>The app's window: the first screen, at the design size where the platform has windows.</summary>
    private static string AppGeneratedCode(ProjectDocument document, string rootNamespace)
    {
        var screen = document.MainScreen;
        return $$"""
            // <auto-generated>
            // {{ProjectExporter.GeneratedMarker}}. This file is replaced on every export.
            // </auto-generated>
            #nullable enable

            namespace {{rootNamespace}};

            public partial class App
            {
                /// <summary>The app's window, showing the first screen at its design size, in the project's theme.</summary>
                private static Window CreateMainWindow()
                {
                    Current!.UserAppTheme = AppTheme.{{AppTheme(document.Theme)}};
                    return new(new {{MainPageClassName}}())
                    {
                        Title = {{Literal(document.Name)}},
                        Width = {{Number(screen.Width + WindowFrameWidth)}},
                        Height = {{Number(screen.Height + WindowFrameHeight)}},
                    };
                }
            }

            """;
    }

    /// <summary>MAUI's name for a theme; Unspecified follows the device.</summary>
    private static string AppTheme(ProjectTheme theme) => theme == ProjectTheme.System ? "Unspecified" : theme.ToString();

    /// <summary>What a Windows window adds around its page: borders and the title bar.</summary>
    public const int WindowFrameWidth = 16;

    public const int WindowFrameHeight = 40;

    private static string AppCode(string rootNamespace) => $$"""
        namespace {{rootNamespace}};

        public partial class App : Application
        {
            public App()
            {
                InitializeComponent();
            }

            protected override Window CreateWindow(IActivationState? activationState) => CreateMainWindow();
        }

        """;

    private static string AppXaml(string rootNamespace) => $$"""
        <?xml version="1.0" encoding="UTF-8" ?>
        <Application xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                     x:Class="{{rootNamespace}}.App">
        </Application>

        """;

    private static string ProgramCode(string rootNamespace) => $$"""
        namespace {{rootNamespace}};

        public static class MauiProgram
        {
            public static MauiApp CreateMauiApp()
            {
                var builder = MauiApp.CreateBuilder();
                builder.UseMauiApp<App>();
                return builder.Build();
            }
        }

        """;

    private static string WindowsAppXaml(string rootNamespace) => $$"""
        <maui:MauiWinUIApplication
            x:Class="{{rootNamespace}}.WinUI.App"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:maui="using:Microsoft.Maui">
        </maui:MauiWinUIApplication>

        """;

    private static string WindowsAppCode(string rootNamespace) => $$"""
        namespace {{rootNamespace}}.WinUI;

        public partial class App : MauiWinUIApplication
        {
            public App()
            {
                InitializeComponent();
            }

            protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
        }

        """;

    private static string WindowsManifest(string rootNamespace) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
          <assemblyIdentity version="1.0.0.0" name="{{rootNamespace}}.WinUI.app" />
          <application xmlns="urn:schemas-microsoft-com:asm.v3">
            <windowsSettings>
              <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
            </windowsSettings>
          </application>
        </assembly>

        """;

    private static string PackageManifest(ProjectDocument document) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package
          xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
          xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
          xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
          IgnorableNamespaces="uap rescap">
          <Identity Name="maui-package-name-placeholder" Publisher="CN=User Name" Version="0.0.0.0" />
          <Properties>
            <DisplayName>$placeholder$</DisplayName>
            <PublisherDisplayName>User Name</PublisherDisplayName>
            <Logo>$placeholder$.png</Logo>
          </Properties>
          <Dependencies>
            <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.17763.0" MaxVersionTested="10.0.19041.0" />
            <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.19041.0" />
          </Dependencies>
          <Resources>
            <Resource Language="x-generate" />
          </Resources>
          <Applications>
            <Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="$targetentrypoint$">
              <uap:VisualElements DisplayName="$placeholder$" Description="{{Attribute(document.Name)}}" Square150x150Logo="$placeholder$.png" Square44x44Logo="$placeholder$.png" BackgroundColor="transparent" />
            </Application>
          </Applications>
          <Capabilities>
            <rescap:Capability Name="runFullTrust" />
          </Capabilities>
        </Package>

        """;

    /// <summary>
    /// A MAUI app for Windows, unpackaged and self-contained so it runs from its build folder.
    /// Other platforms (Android, iOS, Mac Catalyst) can be added to TargetFrameworks.
    /// </summary>
    private static string ProjectFileText(ProjectDocument document, string rootNamespace) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <!-- Windows only; add net10.0-android and others here for more platforms. -->
            <TargetFrameworks>net10.0-windows10.0.19041.0</TargetFrameworks>
            <OutputType>Exe</OutputType>
            <RootNamespace>{{rootNamespace}}</RootNamespace>
            <UseMaui>true</UseMaui>
            <SingleProject>true</SingleProject>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <ApplicationTitle>{{Attribute(document.Name)}}</ApplicationTitle>
            <ApplicationId>com.companyname.{{rootNamespace.ToLowerInvariant()}}</ApplicationId>
            <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
            <ApplicationVersion>1</ApplicationVersion>
            <WindowsPackageType>None</WindowsPackageType>
            <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
            <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())</RuntimeIdentifier>
            <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
            <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
          </PropertyGroup>

          <ItemGroup>
            <!-- Pictures from the design. -->
            <MauiImage Include="Resources\Images\*" />
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
          </ItemGroup>

        </Project>

        """;

    private static string Options(AxisAlignment alignment) => alignment switch
    {
        AxisAlignment.Start => "Start",
        AxisAlignment.End => "End",
        _ => "Fill",
    };

    private static string Bool(bool? value) => value == true ? "True" : "False";

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

    private static string Text(string value) => System.Security.SecurityElement.Escape(value) ?? "";

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
                '{' when escaped.Length == 0 => "{}{",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    private static string Comment(string value) => value.Replace("--", "- -", StringComparison.Ordinal);
}
