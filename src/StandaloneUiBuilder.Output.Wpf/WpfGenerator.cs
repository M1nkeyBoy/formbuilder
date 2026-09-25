using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Wpf;

/// <summary>
/// Generates a complete WPF application from a builder project: a project file, App and a
/// MainWindow whose Canvas holds the screen's controls at their designed positions.
/// Output is deterministic: the same document always produces the same text.
/// </summary>
public static class WpfGenerator
{
    public const string WindowClassName = "MainWindow";

    public const string GeneratedMarker = ProjectExporter.GeneratedMarker;

    // Keep these in step with the designer's ControlFactory so the generated window looks the
    // same as the design surface. A Windows test compares the two.
    private const string LabelPadding = "2,0";

    // Names that would clash with members the generated window already has.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        WindowClassName, "InitializeComponent", "Content", "Title", "Width", "Height", "Name",
        "Resources", "Parent", "Owner", "Icon", "Left", "Top", "Background", "Foreground",
    };

    /// <summary>The one event per control type that gets a hook, and its WPF argument type.</summary>
    private static (string Event, string Args)? EventFor(ControlType type) => type switch
    {
        ControlType.Button => ("Click", "RoutedEventArgs"),
        ControlType.CheckBox => ("Click", "RoutedEventArgs"),
        ControlType.TextBox => ("TextChanged", "TextChangedEventArgs"),
        ControlType.ComboBox => ("SelectionChanged", "SelectionChangedEventArgs"),
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
        var problems = new List<string>();
        if (ControlTree.All(document.Screen.Controls).FirstOrDefault(c => c.Children is not null) is { } container)
        {
            problems.Add($"\"{container.Name}\" is a {container.Type}; containers cannot be exported to WPF yet.");
        }

        foreach (var control in document.Screen.Controls)
        {
            if (CodeNames.CSharpKeywords.Contains(control.Name))
            {
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in WPF code. Rename it.");
            }
            else if (ReservedNames.Contains(control.Name))
            {
                problems.Add($"\"{control.Name}\" clashes with a member of the generated window. Rename the control.");
            }
        }

        // Generated handler and hook names must not collide with a control's field name.
        var generatedMembers = document.Screen.Controls
            .Where(c => EventFor(c.Type) is not null)
            .SelectMany(c => new[] { (Member: HandlerName(c), Owner: c.Name), (Member: HookName(c), Owner: c.Name) })
            .ToDictionary(m => m.Member, m => m.Owner, StringComparer.Ordinal);
        foreach (var control in document.Screen.Controls)
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

    public static IReadOnlyList<GeneratedFile> Generate(ProjectDocument document, string rootNamespace) =>
    [
        new($"{rootNamespace}.csproj", ProjectFileText(rootNamespace), Regenerate: false),
        new("App.xaml", AppXaml(rootNamespace), Regenerate: false),
        new("App.xaml.cs", AppCode(rootNamespace), Regenerate: false),
        new($"{WindowClassName}.xaml", WindowXaml(document, rootNamespace), Regenerate: true),
        new($"{WindowClassName}.xaml.cs", WindowCode(rootNamespace), Regenerate: false),
        new($"{WindowClassName}.Events.g.cs", EventsCode(document, rootNamespace), Regenerate: true),
    ];

    public static string WindowXaml(ProjectDocument document, string rootNamespace)
    {
        var screen = document.Screen;
        var resizable = AnchorLayout.IsResizable(screen);
        var xaml = new StringBuilder();
        xaml.AppendLine($"<!-- {GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        xaml.AppendLine($"     put your own code in {WindowClassName}.xaml.cs. -->");
        xaml.AppendLine($"<Window x:Class=\"{rootNamespace}.{WindowClassName}\"");
        xaml.AppendLine("        xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        xaml.AppendLine("        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        xaml.AppendLine($"        Title=\"{Attribute(document.Name)}\"");
        xaml.AppendLine("        SizeToContent=\"WidthAndHeight\"");
        xaml.AppendLine($"        ResizeMode=\"{(resizable ? "CanResize" : "CanMinimize")}\"");
        xaml.AppendLine("        UseLayoutRounding=\"True\">");

        // The window opens at the design size. If any control follows the right or bottom edge,
        // the window can be enlarged and controls move or stretch with their anchors.
        var size = resizable ? "MinWidth" : "Width";
        var height = resizable ? "MinHeight" : "Height";
        xaml.AppendLine($"    <Grid {size}=\"{Number(screen.Width)}\" {height}=\"{Number(screen.Height)}\">");

        foreach (var control in screen.Controls)
        {
            AppendControl(xaml, screen, control);
        }

        xaml.AppendLine("    </Grid>");
        xaml.AppendLine("</Window>");
        return xaml.ToString();
    }

    private static void AppendControl(StringBuilder xaml, ScreenDocument screen, ControlDocument control)
    {
        var properties = control.Properties;
        var element = control.Type.ToString();
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
        var attributes = new List<string>
        {
            $"x:Name=\"{control.Name}\"",
            $"HorizontalAlignment=\"{Alignment(placement.Horizontal, "Left", "Right")}\"",
            $"VerticalAlignment=\"{Alignment(placement.Vertical, "Top", "Bottom")}\"",
            $"Margin=\"{string.Join(",", margin.Select(Number))}\"",
        };
        if (placement.Width is { } width)
        {
            attributes.Add($"Width=\"{Number(width)}\"");
        }

        if (placement.Height is { } fixedHeight)
        {
            attributes.Add($"Height=\"{Number(fixedHeight)}\"");
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
            case ControlType.TextBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"Text=\"{Attribute(properties.Text ?? "")}\"");
                break;
            case ControlType.CheckBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                attributes.Add($"IsChecked=\"{(properties.IsChecked == true ? "True" : "False")}\"");
                attributes.Add($"Content=\"{Attribute(LiteralAccessText(properties.Text))}\"");
                break;
            case ControlType.ComboBox:
                attributes.Add("VerticalContentAlignment=\"Center\"");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type.");
        }

        if (EventFor(control.Type) is { } hook)
        {
            attributes.Add($"{hook.Event}=\"{HandlerName(control)}\"");
        }

        var items = control.Type == ControlType.ComboBox ? properties.Items ?? [] : [];
        var opening = $"        <{element} {string.Join(" ", attributes)}";
        if (items.Count == 0)
        {
            xaml.AppendLine(opening + " />");
            return;
        }

        xaml.AppendLine(opening + ">");
        foreach (var item in items)
        {
            xaml.AppendLine($"            <ComboBoxItem Content=\"{Attribute(item)}\" />");
        }

        xaml.AppendLine($"        </{element}>");
    }

    /// <summary>
    /// The regenerated half of the event wiring: a private handler for each control that the
    /// XAML refers to, which calls a partial method the developer may implement. Unimplemented
    /// hooks compile away, so adding or removing controls never breaks the build.
    /// </summary>
    public static string EventsCode(ProjectDocument document, string rootNamespace)
    {
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {WindowClassName}.xaml.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClick(RoutedEventArgs e) { MessageBox.Show(\"Submitted\"); }");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine("using System.Windows;");
        code.AppendLine("using System.Windows.Controls;");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"public partial class {WindowClassName}");
        code.AppendLine("{");

        var first = true;
        foreach (var control in document.Screen.Controls)
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
            code.AppendLine($"    private void {HandlerName(control)}(object sender, {e.Args} e) => {HookName(control)}(e);");
            code.AppendLine();
            code.AppendLine($"    /// <summary>{control.Type} \"{control.Name}\": {e.Event}.</summary>");
            code.AppendLine($"    partial void {HookName(control)}({e.Args} e);");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    private static string WindowCode(string rootNamespace) => $$"""
        using System.Windows;

        namespace {{rootNamespace}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The controls are declared in {{WindowClassName}}.xaml, which is regenerated on every export.
        // To respond to a control, implement its hook from {{WindowClassName}}.Events.g.cs, for example:
        //     partial void OnSubmitButtonClick(RoutedEventArgs e) { MessageBox.Show("Submitted"); }
        public partial class {{WindowClassName}} : Window
        {
            public {{WindowClassName}}()
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
