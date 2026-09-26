using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Wpf;

/// <summary>
/// Generates a complete WPF application from a builder project: a project file, App, and a
/// window for each screen that holds its controls at their designed positions. The first
/// screen is MainWindow, which the application opens with; the others are named after
/// themselves (SettingsWindow) for the developer's code to open.
/// Output is deterministic: the same document always produces the same text.
/// </summary>
public static class WpfGenerator
{
    /// <summary>The first screen's window, which the application opens with.</summary>
    public const string WindowClassName = "MainWindow";

    private const string ClassSuffix = "Window";

    public const string GeneratedMarker = ProjectExporter.GeneratedMarker;

    // Keep these in step with the designer's ControlFactory so the generated window looks the
    // same as the design surface. A Windows test compares the two.
    private const string LabelPadding = "2,0";

    // Names that would clash with members the generated window already has. The window's own
    // class name is checked separately.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "InitializeComponent", "ViewModel", "Content", "Title", "Width", "Height", "Name",
        "Resources", "Parent", "Owner", "Icon", "Left", "Top", "Background", "Foreground",
    };

    /// <summary>The one event per control type that gets a hook, and its WPF argument type.</summary>
    private static (string Event, string Args)? EventFor(ControlType type) => type switch
    {
        ControlType.Button => ("Click", "RoutedEventArgs"),
        ControlType.CheckBox => ("Click", "RoutedEventArgs"),
        ControlType.TextBox => ("TextChanged", "TextChangedEventArgs"),
        ControlType.ComboBox => ("SelectionChanged", "SelectionChangedEventArgs"),
        ControlType.RadioButton => ("Click", "RoutedEventArgs"),
        ControlType.ListBox => ("SelectionChanged", "SelectionChangedEventArgs"),
        ControlType.Slider => ("ValueChanged", "RoutedPropertyChangedEventArgs<double>"),
        ControlType.DatePicker => ("SelectedDateChanged", "SelectionChangedEventArgs"),
        ControlType.PasswordBox => ("PasswordChanged", "RoutedEventArgs"),
        ControlType.TabControl => ("SelectionChanged", "SelectionChangedEventArgs"),
        _ => null,
    };

    /// <summary>The private handler wired up in XAML, for example <c>SubmitButton_Click</c>.</summary>
    public static string HandlerName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"{control.Name}_{e.Event}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    /// <summary>The partial method a developer implements, for example <c>OnSubmitButtonClick</c>.</summary>
    public static string HookName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"On{control.Name}{e.Event}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    /// <summary>
    /// Returns reasons the document cannot be exported to WPF as it stands, or an empty list.
    /// </summary>
    public static IReadOnlyList<string> Check(ProjectDocument document)
    {
        var problems = CodeNames.CheckScreenClassNames(document, ClassSuffix, "window").ToList();
        foreach (var screen in document.Screens)
        {
            // With one screen, messages read as before; with several, they say which screen.
            var prefix = document.Screens.Count > 1 ? $"Screen \"{screen.Name}\": " : "";
            problems.AddRange(CheckScreen(ClassName(document, screen), screen).Select(p => prefix + p));
        }

        return problems;
    }

    /// <summary>The class a screen's window gets: MainWindow for the first screen, otherwise SettingsWindow and so on.</summary>
    public static string ClassName(ProjectDocument document, ScreenDocument screen) =>
        CodeNames.ScreenClassName(document, screen, ClassSuffix);

    private static List<string> CheckScreen(string className, ScreenDocument screen)
    {
        var problems = new List<string>();
        var all = ControlTree.All(screen.Controls).ToList();
        foreach (var control in all)
        {
            if (CodeNames.CSharpKeywords.Contains(control.Name))
            {
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in WPF code. Rename it.");
            }
            else if (ReservedNames.Contains(control.Name) || control.Name == className)
            {
                problems.Add($"\"{control.Name}\" clashes with a member of the generated window. Rename the control.");
            }
        }

        // Generated handler and hook names must not collide with a control's field name.
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

    /// <summary>Turns a project name into a C# namespace: "Customer form" becomes "CustomerForm".</summary>
    public static string ToNamespace(string projectName) => CodeNames.ToNamespace(projectName);

    public static IReadOnlyList<GeneratedFile> Generate(ProjectDocument document, string rootNamespace)
    {
        List<GeneratedFile> files =
        [
            new($"{rootNamespace}.csproj", ProjectFileText(rootNamespace), Regenerate: false),
            new("App.xaml", AppXaml(rootNamespace), Regenerate: false),
            new("App.xaml.cs", AppCode(rootNamespace), Regenerate: false),
        ];

        foreach (var screen in document.Screens)
        {
            var className = ClassName(document, screen);
            files.Add(new($"{className}.xaml", WindowXaml(document, screen, rootNamespace), Regenerate: true));
            files.Add(new($"{className}.xaml.cs", WindowCode(rootNamespace, className), Regenerate: false));
            files.Add(new($"{className}.Events.g.cs", EventsCode(document, screen, rootNamespace), Regenerate: true));
            if (DataBindings.HasViewModel(screen))
            {
                files.Add(new(Output.ViewModelCode.FileName(document, screen), ViewModelCode(document, screen, rootNamespace), Regenerate: true));
            }

            if (Output.ViewModelCode.UserCode(document, screen, rootNamespace) is { } userCode)
            {
                files.Add(new(Output.ViewModelCode.CodeFileName(document, screen), userCode, Regenerate: true));
            }
        }

        files.AddRange(CodeNames.ImageFiles(document));
        return files;
    }

    /// <summary>
    /// A screen's view model, with WPF's types: a Slider's value is a double, a DatePicker's date
    /// a nullable DateTime, and a ComboBox's or ListBox's choice the chosen item's text, or null.
    /// </summary>
    public static string ViewModelCode(ProjectDocument document, ScreenDocument screen, string rootNamespace) =>
        Output.ViewModelCode.Generate(document, screen, rootNamespace,
            kind => kind switch
            {
                BindingKind.Text => "string",
                BindingKind.Flag => "bool",
                BindingKind.Number => "double",
                BindingKind.Choice => "string?",
                _ => "DateTime?",
            },
            property => property.Kind switch
            {
                BindingKind.Text => Output.ViewModelCode.TextLiteral(property),
                BindingKind.Flag => Output.ViewModelCode.FlagLiteral(property),
                BindingKind.Number => Output.ViewModelCode.NumberLiteral(property),
                _ => null,
            });

    /// <summary>The first screen's window.</summary>
    public static string WindowXaml(ProjectDocument document, string rootNamespace) =>
        WindowXaml(document, document.MainScreen, rootNamespace);

    public static string WindowXaml(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        // Text on the design's own backgrounds stays readable in a dark theme.
        screen = ThemeContrast.Apply(document.Theme, screen);
        var className = ClassName(document, screen);
        var resizable = AnchorLayout.IsResizable(screen);
        var theme = document.Theme;
        var xaml = new StringBuilder();
        xaml.AppendLine($"<!-- {GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        xaml.AppendLine($"     put your own code in {className}.xaml.cs. -->");
        xaml.AppendLine($"<Window x:Class=\"{rootNamespace}.{className}\"");
        xaml.AppendLine("        xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        xaml.AppendLine("        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        xaml.AppendLine($"        Title=\"{Attribute(WindowTitle(document, screen))}\"");
        xaml.AppendLine("        SizeToContent=\"WidthAndHeight\"");
        xaml.AppendLine($"        ResizeMode=\"{(resizable ? "CanResize" : "CanMinimize")}\"");
        if (theme != ProjectTheme.Light)
        {
            xaml.AppendLine($"        ThemeMode=\"{ThemeMode(document.Theme)}\"");
        }

        // Bindings look up their properties in the window's view model.
        if (DataBindings.HasViewModel(screen))
        {
            xaml.AppendLine("        DataContext=\"{Binding ViewModel, RelativeSource={RelativeSource Self}}\"");
        }

        xaml.AppendLine("        UseLayoutRounding=\"True\">");

        // The window opens at the design size. If any control follows the right or bottom edge,
        // the window can be enlarged and controls move or stretch with their anchors.
        var size = resizable ? "MinWidth" : "Width";
        var height = resizable ? "MinHeight" : "Height";
        xaml.AppendLine($"    <Grid {size}=\"{Number(screen.Width)}\" {height}=\"{Number(screen.Height)}\">");

        foreach (var control in screen.Controls)
        {
            AppendControl(xaml, screen, theme, control);
        }

        xaml.AppendLine("    </Grid>");
        xaml.AppendLine("</Window>");
        return xaml.ToString();
    }

    /// <summary>A control placed directly on the screen: alignment and margins from its anchors.</summary>
    private static void AppendControl(StringBuilder xaml, ScreenDocument screen, ProjectTheme theme, ControlDocument control)
    {
        var placement = AnchorLayout.Place(screen, control);

        // Margins count only on the anchored sides; a fixed size is written only when the
        // control does not stretch along that axis.
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

        AppendElement(xaml, screen, theme, control, layout, depth: 2);
    }

    /// <summary>
    /// Writes one control with the layout attributes its parent needs, then, for a container,
    /// its children with theirs: a StackPanel child keeps its size along the stack, stretches
    /// across it and has the spacing as a leading margin; a Grid child fills its cell.
    /// </summary>
    private static void AppendElement(StringBuilder xaml, ScreenDocument screen, ProjectTheme theme, ControlDocument control, List<string> layout, int depth)
    {
        if (control.Type == ControlType.GroupBox)
        {
            AppendGroupBox(xaml, screen, theme, control, layout, depth);
            return;
        }

        if (control.Type == ControlType.Image)
        {
            AppendImage(xaml, screen, theme, control, layout, depth);
            return;
        }

        if (control.Type == ControlType.TabControl)
        {
            AppendTabControl(xaml, screen, theme, control, layout, depth);
            return;
        }

        var indent = new string(' ', depth * 4);
        var properties = control.Properties;
        var element = control.Type.ToString();
        var attributes = new List<string> { $"x:Name=\"{control.Name}\"" };
        attributes.AddRange(layout);

        // The Fluent styles of the dark and system themes give controls minimum sizes (a text
        // box is at least 32 high); the design's sizes stand.
        if (theme != ProjectTheme.Light && !ControlCatalog.Get(control.Type).IsContainer)
        {
            attributes.Add("MinWidth=\"0\"");
            attributes.Add("MinHeight=\"0\"");
        }

        switch (control.Type)
        {
            case ControlType.Label:
                attributes.Add($"Padding=\"{LabelPadding}\"");
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"Content=\"{Attribute(LiteralAccessText(properties.Text))}\"");
                break;
            case ControlType.Button:
                attributes.Add($"Content=\"{Attribute(LiteralAccessText(properties.Text))}\"");
                break;
            case ControlType.TextBox when properties.IsMultiline == true:
                attributes.Add("VerticalContentAlignment=\"Top\"");
                attributes.Add("AcceptsReturn=\"True\"");
                attributes.Add("TextWrapping=\"Wrap\"");
                attributes.Add("VerticalScrollBarVisibility=\"Auto\"");
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
            case ControlType.RadioButton:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"IsChecked=\"{(properties.IsChecked == true ? "True" : "False")}\"");
                attributes.Add($"Content=\"{Attribute(LiteralAccessText(properties.Text))}\"");
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
                    // Whole numbers, as designed.
                    attributes.Add("IsSnapToTickEnabled=\"True\"");
                    attributes.Add("TickFrequency=\"1\"");
                }

                break;
            case ControlType.CheckBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"IsChecked=\"{(properties.IsChecked == true ? "True" : "False")}\"");
                attributes.Add($"Content=\"{Attribute(LiteralAccessText(properties.Text))}\"");
                break;
            case ControlType.ComboBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                break;
            case ControlType.StackPanel:
                attributes.Add($"Orientation=\"{(properties.Orientation == StackOrientation.Horizontal ? "Horizontal" : "Vertical")}\"");
                attributes.Add("ClipToBounds=\"True\"");
                break;
            case ControlType.Grid:
                attributes.Add("ClipToBounds=\"True\"");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type.");
        }

        if (properties.Binding is { } binding && BindingAttributes(control.Type, binding) is var (replaces, bound))
        {
            attributes.RemoveAll(a => replaces is not null && a.StartsWith(replaces + "=", StringComparison.Ordinal));
            attributes.AddRange(bound);
        }

        if (properties.EnabledBinding is { } enabled)
        {
            attributes.Add($"IsEnabled=\"{{Binding {enabled}}}\"");
        }

        attributes.AddRange(StyleAttributes(properties));
        attributes.AddRange(TabAttributes(screen, control));
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
            AppendStackChildren(xaml, screen, theme, control, depth + 1);
        }
        else if (control.Type == ControlType.Grid)
        {
            // Row and column sizes use WPF's own notation: "100" fixed, "*" and "2*" shares.
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
                AppendElement(xaml, screen, theme, child, cell, depth + 1);
            }
        }

        xaml.AppendLine($"{indent}</{element}>");
    }

    /// <summary>
    /// An Image, in a Grid that has its designed box. An Image that keeps its picture's shape
    /// shrinks to the picture, so on its own it would follow its anchors to one edge; in the
    /// Grid it is centred in the box, as the other targets show it.
    /// </summary>
    private static void AppendImage(StringBuilder xaml, ScreenDocument screen, ProjectTheme theme, ControlDocument control, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var properties = control.Properties;
        var source = properties.ImageData is not null ? $" Source=\"{ImageFile.ExportPath(screen, control)}\"" : "";
        xaml.AppendLine($"{indent}<Grid {string.Join(" ", layout)}>");
        xaml.AppendLine($"{indent}    <Image x:Name=\"{control.Name}\"{source} Stretch=\"{properties.Stretch ?? ImageStretch.Uniform}\" />");
        xaml.AppendLine($"{indent}</Grid>");
    }

    /// <summary>
    /// With a tab order set on the screen, each control Tab visits gets its place in it as
    /// TabIndex; WPF orders Tab by TabIndex across the whole window. A control with parts of
    /// its own to tab through (a DatePicker's text and button, a TabControl's tabs) keeps them
    /// together as a local group.
    /// </summary>
    private static IEnumerable<string> TabAttributes(ScreenDocument screen, ControlDocument control)
    {
        if (screen.TabOrder is null || !TabSequence.Indexes(screen).TryGetValue(control.Id, out var index))
        {
            yield break;
        }

        yield return $"TabIndex=\"{Number(index)}\"";
        if (control.Type is ControlType.DatePicker or ControlType.TabControl)
        {
            yield return "KeyboardNavigation.TabNavigation=\"Local\"";
        }
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
    /// A StackPanel's children: each keeps its size along the stack, stretches across it and
    /// has the spacing as a leading margin.
    /// </summary>
    private static void AppendStackChildren(StringBuilder xaml, ScreenDocument screen, ProjectTheme theme, ControlDocument stack, int depth)
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

            AppendElement(xaml, screen, theme, child, childLayout, depth);
        }
    }

    /// <summary>
    /// A GroupBox: a Grid in its place holding the real GroupBox, which draws the frame and
    /// title, and on top of it a StackPanel inset by <see cref="ContainerLayout.GroupBoxInset"/>
    /// for the children. The fixed inset puts children exactly where the design has them,
    /// whatever the theme's frame looks like.
    /// </summary>
    private static void AppendGroupBox(StringBuilder xaml, ScreenDocument screen, ProjectTheme theme, ControlDocument group, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var properties = group.Properties;
        var (left, top, right, bottom) = ContainerLayout.GroupBoxInset;
        xaml.AppendLine($"{indent}<Grid {string.Join(" ", layout)}>");
        var style = string.Concat(StyleAttributes(properties).Select(a => " " + a));
        xaml.AppendLine($"{indent}    <GroupBox x:Name=\"{group.Name}\" Header=\"{Attribute(properties.Text ?? "")}\"{style} />");
        var orientation = properties.Orientation == StackOrientation.Horizontal ? "Horizontal" : "Vertical";
        var margin = string.Join(",", new[] { left, top, right, bottom }.Select(Number));
        var opening = $"{indent}    <StackPanel Margin=\"{margin}\" Orientation=\"{orientation}\" ClipToBounds=\"True\"";
        if (group.Children is not { Count: > 0 })
        {
            xaml.AppendLine(opening + " />");
        }
        else
        {
            xaml.AppendLine(opening + ">");
            AppendStackChildren(xaml, screen, theme, group, depth + 2);
            xaml.AppendLine($"{indent}    </StackPanel>");
        }

        xaml.AppendLine($"{indent}</Grid>");
    }

    /// <summary>
    /// A TabControl: a Grid in its place holding the real TabControl, with an empty TabItem for
    /// each page's tab, and on top of it each page as a StackPanel inset by
    /// <see cref="ContainerLayout.TabControlInset"/>, only the selected one visible. As with a
    /// GroupBox, the fixed inset puts pages exactly where the design has them; the generated
    /// SelectionChanged handler shows the page whose tab is chosen.
    /// </summary>
    private static void AppendTabControl(StringBuilder xaml, ScreenDocument screen, ProjectTheme theme, ControlDocument tabs, List<string> layout, int depth)
    {
        var indent = new string(' ', depth * 4);
        var pages = tabs.Children ?? [];
        var shown = ContainerLayout.ShownTab(tabs);
        var (left, top, right, bottom) = ContainerLayout.TabControlInset;
        xaml.AppendLine($"{indent}<Grid {string.Join(" ", layout)}>");
        var tabAttributes = string.Concat(TabAttributes(screen, tabs).Select(a => " " + a));
        var opening = $"{indent}    <TabControl x:Name=\"{tabs.Name}\" SelectedIndex=\"{Number(pages.Count > 0 ? shown : -1)}\"{tabAttributes} SelectionChanged=\"{HandlerName(tabs)}\"";
        if (pages.Count == 0)
        {
            xaml.AppendLine(opening + " />");
        }
        else
        {
            xaml.AppendLine(opening + ">");
            foreach (var page in pages)
            {
                xaml.AppendLine($"{indent}        <TabItem Header=\"{Attribute(LiteralAccessText(page.Properties.Text))}\" />");
            }

            xaml.AppendLine($"{indent}    </TabControl>");
        }

        var margin = string.Join(",", new[] { left, top, right, bottom }.Select(Number));
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var properties = page.Properties;
            var orientation = properties.Orientation == StackOrientation.Horizontal ? "Horizontal" : "Vertical";
            var visibility = i == shown ? "" : " Visibility=\"Collapsed\"";
            var style = string.Concat(StyleAttributes(properties).Select(a => " " + a));
            var pageOpening = $"{indent}    <StackPanel x:Name=\"{page.Name}\" Margin=\"{margin}\" Orientation=\"{orientation}\" ClipToBounds=\"True\"{visibility}{style}";
            if (page.Children is not { Count: > 0 })
            {
                xaml.AppendLine(pageOpening + " />");
                continue;
            }

            xaml.AppendLine(pageOpening + ">");
            AppendStackChildren(xaml, screen, theme, page, depth + 2);
            xaml.AppendLine($"{indent}    </StackPanel>");
        }

        xaml.AppendLine($"{indent}</Grid>");
    }

    /// <summary>
    /// The regenerated half of the event wiring: a private handler for each control that the
    /// XAML refers to, which calls a partial method the developer may implement. Unimplemented
    /// hooks compile away, so adding or removing controls never breaks the build.
    /// </summary>
    public static string EventsCode(ProjectDocument document, string rootNamespace) =>
        EventsCode(document, document.MainScreen, rootNamespace);

    public static string EventsCode(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        var className = ClassName(document, screen);
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {className}.xaml.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClick(RoutedEventArgs e) { MessageBox.Show(\"Submitted\"); }");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine("using System.Windows;");
        code.AppendLine("using System.Windows.Controls;");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"public partial class {className}");
        code.AppendLine("{");

        var first = true;
        if (DataBindings.HasViewModel(screen))
        {
            code.AppendLine($"    /// <summary>The values the window's controls are bound to; the window's DataContext.</summary>");
            code.AppendLine($"    public {Output.ViewModelCode.ClassName(document, screen)} ViewModel {{ get; }} = new();");
            first = false;
        }

        foreach (var control in ControlTree.All(screen.Controls))
        {
            if (EventFor(control.Type) is not { } e)
            {
                continue;
            }

            if (!first)
            {
                code.AppendLine();
            }

            first = false;
            if (control.Type == ControlType.TabControl)
            {
                // The pages are beside the TabControl, not in it, so the handler shows the one
                // whose tab is chosen. It also runs while the window loads, before the pages exist.
                var pages = control.Children ?? [];
                code.AppendLine($"    private void {HandlerName(control)}(object sender, {e.Args} e)");
                code.AppendLine("    {");
                if (pages.Count > 0)
                {
                    code.AppendLine($"        if ({pages[^1].Name} is not null)");
                    code.AppendLine("        {");
                    for (var i = 0; i < pages.Count; i++)
                    {
                        code.AppendLine($"            {pages[i].Name}.Visibility = {control.Name}.SelectedIndex == {i} ? Visibility.Visible : Visibility.Collapsed;");
                    }

                    code.AppendLine("        }");
                    code.AppendLine();
                }

                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine("    }");
            }
            else if (ActionStatement(document, control) is not null || control.Properties.Command is not null)
            {
                // The hook runs first, then the button's command, then its action from the design.
                code.AppendLine($"    private void {HandlerName(control)}(object sender, {e.Args} e)");
                code.AppendLine("    {");
                code.AppendLine($"        {HookName(control)}(e);");
                foreach (var statement in new[] { Output.ViewModelCode.CommandStatement(control), ActionStatement(document, control) }.OfType<string>())
                {
                    code.AppendLine($"        {statement}");
                }

                code.AppendLine("    }");
            }
            else if (control.Type == ControlType.PasswordBox && control.Properties.Binding is { } binding)
            {
                code.AppendLine($"    private void {HandlerName(control)}(object sender, {e.Args} e)");
                code.AppendLine("    {");
                code.AppendLine($"        ViewModel.{binding} = {control.Name}.Password;");
                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine("    }");
            }
            else
            {
                code.AppendLine($"    private void {HandlerName(control)}(object sender, {e.Args} e) => {HookName(control)}(e);");
            }

            code.AppendLine();
            code.AppendLine($"    /// <summary>{control.Type} \"{control.Name}\": {e.Event}.</summary>");
            code.AppendLine($"    partial void {HookName(control)}({e.Args} e);");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    /// <summary>
    /// The first screen's window shows the project name; other screens show their own name,
    /// the only title the builder has for them.
    /// </summary>
    private static string WindowTitle(ProjectDocument document, ScreenDocument screen) =>
        screen.Id == document.MainScreen.Id ? document.Name : screen.Name;

    /// <summary>What a button's action does: show another screen's window as a dialog, or close this one.</summary>
    private static string? ActionStatement(ProjectDocument document, ControlDocument control) =>
        control.Properties.OpensScreen is { } id && document.FindScreen(id) is { } target
            ? $"new {ClassName(document, target)} {{ Owner = this }}.ShowDialog();"
            : control.Properties.ClosesScreen == true ? "Close();"
            : null;

    private static string WindowCode(string rootNamespace, string className) => $$"""
        using System.Windows;

        namespace {{rootNamespace}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The controls are declared in {{className}}.xaml, which is regenerated on every export.
        // To respond to a control, implement its hook from {{className}}.Events.g.cs, for example:
        //     partial void OnSubmitButtonClick(RoutedEventArgs e) { MessageBox.Show("Submitted"); }
        // To show another screen, create its window: new SettingsWindow { Owner = this }.ShowDialog();
        public partial class {{className}} : Window
        {
            public {{className}}()
            {
                InitializeComponent();
            }
        }

        """;

    private static string AppXaml(string rootNamespace) => $$"""
        <Application x:Class="{{rootNamespace}}.App"
                     xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     StartupUri="{{WindowClassName}}.xaml">
        </Application>

        """;

    private static string AppCode(string rootNamespace) => $$"""
        using System.Windows;

        namespace {{rootNamespace}};

        public partial class App : Application
        {
        }

        """;

    private static string ProjectFileText(string rootNamespace) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <UseWPF>true</UseWPF>
            <RootNamespace>{{rootNamespace}}</RootNamespace>
          </PropertyGroup>

          <ItemGroup>
            <!-- Pictures from the design, built into the application. -->
            <Resource Include="Assets\**" />
          </ItemGroup>

        </Project>

        """;

    /// <summary>
    /// Label, Button and CheckBox treat "_" as an access-key marker. The designer shows text
    /// literally, so each underscore is doubled to display as itself.
    /// </summary>
    private static string LiteralAccessText(string? text) => (text ?? "").Replace("_", "__", StringComparison.Ordinal);

    private static string Alignment(AxisAlignment alignment, string start, string end) => alignment switch
    {
        AxisAlignment.Start => start,
        AxisAlignment.End => end,
        _ => "Stretch",
    };

    /// <summary>
    /// The attributes that bind a control's value to a view model property, and the design-value
    /// attribute they replace. A PasswordBox's password cannot be bound in WPF; its
    /// PasswordChanged handler copies it into the view model instead.
    /// </summary>
    private static (string? Replaces, string[] Attributes)? BindingAttributes(ControlType type, string name) => type switch
    {
        ControlType.Label => ("Content", [$"Content=\"{{Binding {name}, Mode=OneWay}}\""]),
        ControlType.TextBox => ("Text", [$"Text=\"{{Binding {name}, UpdateSourceTrigger=PropertyChanged}}\""]),
        ControlType.CheckBox or ControlType.RadioButton => ("IsChecked", [$"IsChecked=\"{{Binding {name}}}\""]),
        ControlType.Slider => ("Value", [$"Value=\"{{Binding {name}}}\""]),
        ControlType.ProgressBar => ("Value", [$"Value=\"{{Binding {name}, Mode=OneWay}}\""]),
        ControlType.ComboBox or ControlType.ListBox => (null, ["SelectedValuePath=\"Content\"", $"SelectedValue=\"{{Binding {name}}}\""]),
        ControlType.DatePicker => (null, [$"SelectedDate=\"{{Binding {name}}}\""]),
        _ => null,
    };

    /// <summary>The window's Fluent theme: dark, or following Windows' app mode.</summary>
    private static string ThemeMode(ProjectTheme theme) => theme == ProjectTheme.Dark ? "Dark" : "System";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

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
