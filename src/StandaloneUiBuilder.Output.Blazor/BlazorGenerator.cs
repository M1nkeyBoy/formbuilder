using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.Blazor;

/// <summary>
/// Generates a Blazor web app (interactive server rendering) from a builder project: one
/// routable page per screen, with every control placed by CSS exactly where the design has it.
/// The first screen is <c>MainPage</c> at "/"; the others are named after themselves
/// (<c>SettingsPage</c> at "/settings"). Output is deterministic.
/// </summary>
/// <remarks>
/// Layout follows the Core rules with plain CSS: controls on the screen are absolutely
/// positioned from their anchors (left/right/top/bottom plus width/height), a StackPanel is a
/// flex box, a Grid is a CSS grid with the same fixed and shared (<c>fr</c>) tracks, and a
/// GroupBox is a framed box whose children sit in a flex box at the fixed inset. A TabControl
/// is a row of tab buttons over a frame, with each page a flex box at its fixed inset, hidden
/// unless its tab is chosen.
/// </remarks>
public static class BlazorGenerator
{
    public const string MainPageClassName = "MainPage";

    /// <summary>The namespace pages live in, below the root namespace.</summary>
    public const string PagesNamespaceSuffix = ".Components.Pages";

    private const string ClassSuffix = "Page";

    // Members a page already has (from ComponentBase, or injected by the generated code) that a
    // control's field would clash with. The page's own class name is checked separately.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "Navigation", "JS", "BuildRenderTree", "OnInitialized", "OnInitializedAsync", "OnParametersSet",
        "OnParametersSetAsync", "OnAfterRender", "OnAfterRenderAsync", "StateHasChanged", "ShouldRender",
        "InvokeAsync", "DispatchExceptionAsync", "SetParametersAsync", "Assets", "RendererInfo", "AssignedRenderMode",
    };

    /// <summary>A page's path within the project, for example <c>Components/Pages/MainPage.razor</c>.</summary>
    public static string PagePath(string className, string extension) => $"Components/Pages/{className}{extension}";

    /// <summary>The page class for a screen: MainPage for the first, otherwise SettingsPage and so on.</summary>
    public static string ClassName(ProjectDocument document, ScreenDocument screen) =>
        CodeNames.ScreenClassName(document, screen, ClassSuffix);

    /// <summary>The address of a screen's page: "/" for the first, otherwise "/settings" and so on.</summary>
    public static string Route(ProjectDocument document, ScreenDocument screen) =>
        screen.Id == document.MainScreen.Id ? "/" : "/" + screen.Name.ToLowerInvariant();

    /// <summary>The one event per control type that gets a hook, named as in the WPF output.</summary>
    private static string? EventFor(ControlType type) => type switch
    {
        ControlType.Button or ControlType.CheckBox or ControlType.RadioButton => "Click",
        ControlType.TextBox => "TextChanged",
        ControlType.PasswordBox => "PasswordChanged",
        ControlType.ComboBox or ControlType.ListBox => "SelectionChanged",
        ControlType.Slider => "ValueChanged",
        ControlType.DatePicker => "SelectedDateChanged",
        ControlType.TabControl => "SelectionChanged",
        _ => null,
    };

    public static string HandlerName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"{control.Name}_{e}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    public static string HookName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"On{control.Name}{e}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    /// <summary>
    /// The C# type and initial value of the field that holds what a control shows, for the
    /// types that have one. The field is named after the control.
    /// </summary>
    private static (string Type, string Initial)? ValueField(ControlDocument control)
    {
        var properties = control.Properties;
        return control.Type switch
        {
            ControlType.TextBox => ("string", Literal(properties.Text ?? "")),
            ControlType.PasswordBox or ControlType.ComboBox or ControlType.ListBox => ("string", "\"\""),
            ControlType.CheckBox or ControlType.RadioButton => ("bool", properties.IsChecked == true ? "true" : "false"),
            ControlType.Slider or ControlType.ProgressBar => ("int", Number(properties.Value ?? 0)),
            ControlType.DatePicker => ("DateOnly?", "null"),
            ControlType.TabControl => ("int", Number(ContainerLayout.ShownTab(control))),
            _ => null,
        };
    }

    /// <summary>Returns reasons the document cannot be exported to Blazor as it stands, or an empty list.</summary>
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
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in Blazor code. Rename it.");
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
            new($"{rootNamespace}.csproj", ProjectFileText(rootNamespace), Regenerate: false),
            new("Program.cs", ProgramCode(rootNamespace), Regenerate: false),
            new("Components/App.razor", AppRazor(), Regenerate: false),
            new("Components/Routes.razor", RoutesRazor(), Regenerate: false),
            new("Components/_Imports.razor", ImportsRazor(rootNamespace), Regenerate: false),
            new("Components/Layout/MainLayout.razor", LayoutRazor(), Regenerate: false),
            new("wwwroot/app.css", AppCss(), Regenerate: false),
            new("wwwroot/uib.css", BuilderCss(), Regenerate: true),
        ];

        foreach (var screen in document.Screens)
        {
            var className = ClassName(document, screen);
            files.Add(new(PagePath(className, ".razor"), PageRazor(document, screen), Regenerate: true));
            files.Add(new(PagePath(className, ".razor.cs"), PageCode(rootNamespace, className), Regenerate: false));
            files.Add(new(PagePath(className, ".Events.g.cs"), EventsCode(document, screen, rootNamespace), Regenerate: true));
        }

        files.AddRange(CodeNames.ImageFiles(document, folder: "wwwroot/"));
        return files;
    }

    /// <summary>The first screen's page markup.</summary>
    public static string PageRazor(ProjectDocument document) => PageRazor(document, document.MainScreen);

    public static string PageRazor(ProjectDocument document, ScreenDocument screen)
    {
        var className = ClassName(document, screen);
        var markup = new StringBuilder();
        markup.AppendLine($"@page \"{Route(document, screen)}\"");
        markup.AppendLine($"@* {ProjectExporter.GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        markup.AppendLine($"   put your own code in {className}.razor.cs. *@");
        markup.AppendLine();
        markup.AppendLine($"<PageTitle>{Text(screen.Id == document.MainScreen.Id ? document.Name : screen.Name)}</PageTitle>");
        markup.AppendLine();

        // The page has the design size. If any control follows the right or bottom edge, it
        // fills the browser window instead, never smaller than the design size, and controls
        // move or stretch with their anchors.
        var size = AnchorLayout.IsResizable(screen)
            ? $"width:100%;height:100vh;min-width:{Px(screen.Width)};min-height:{Px(screen.Height)}"
            : $"width:{Px(screen.Width)};height:{Px(screen.Height)}";
        markup.AppendLine($"<div class=\"uib-screen\" style=\"{size}\">");
        foreach (var control in screen.Controls)
        {
            AppendElement(markup, screen, control, RootStyle(screen, control), group: "screen", depth: 1);
        }

        markup.AppendLine("</div>");
        return markup.ToString();
    }

    /// <summary>A control on the screen: absolutely positioned from its anchors.</summary>
    private static List<string> RootStyle(ScreenDocument screen, ControlDocument control)
    {
        var placement = AnchorLayout.Place(screen, control);
        var style = new List<string> { "position:absolute" };
        switch (placement.Horizontal)
        {
            case AxisAlignment.Start:
                style.Add($"left:{Px(placement.MarginLeft)}");
                style.Add($"width:{Px(placement.Width ?? 0)}");
                break;
            case AxisAlignment.End:
                style.Add($"right:{Px(placement.MarginRight)}");
                style.Add($"width:{Px(placement.Width ?? 0)}");
                break;
            default:
                style.Add($"left:{Px(placement.MarginLeft)}");
                style.Add($"right:{Px(placement.MarginRight)}");
                break;
        }

        switch (placement.Vertical)
        {
            case AxisAlignment.Start:
                style.Add($"top:{Px(placement.MarginTop)}");
                style.Add($"height:{Px(placement.Height ?? 0)}");
                break;
            case AxisAlignment.End:
                style.Add($"bottom:{Px(placement.MarginBottom)}");
                style.Add($"height:{Px(placement.Height ?? 0)}");
                break;
            default:
                style.Add($"top:{Px(placement.MarginTop)}");
                style.Add($"bottom:{Px(placement.MarginBottom)}");
                break;
        }

        return style;
    }

    /// <summary>
    /// Writes one control with the layout its parent needs, then, for a container, its
    /// children with theirs. <paramref name="group"/> names the radio button group: the
    /// container a RadioButton is in, or the screen.
    /// </summary>
    /// <param name="hiddenUnless">For a tab page, the C# condition under which it is hidden.</param>
    private static void AppendElement(StringBuilder markup, ScreenDocument screen, ControlDocument control, List<string> style, string group, int depth, string hiddenUnless = "false")
    {
        var indent = new string(' ', depth * 4);
        var properties = control.Properties;
        var name = control.Name;

        // Text size, weight and colours go in the same style attribute. A GroupBox's apply to its
        // title and frame only, not to the controls inside, as in WPF.
        if (control.Type != ControlType.GroupBox)
        {
            style = [.. style, .. StyleRules(properties, background: true)];
        }

        // A container's own layout goes in the same style attribute as its placement.
        if (control.Type is ControlType.StackPanel or ControlType.TabPage)
        {
            style = [.. style, StackStyle(properties)];
        }
        else if (control.Type == ControlType.Grid)
        {
            style =
            [
                .. style,
                "grid-template-rows:" + string.Join(" ", GridTrackSize.Resolve(properties.RowSizes, properties.Rows ?? 1).Select(Track)),
                "grid-template-columns:" + string.Join(" ", GridTrackSize.Resolve(properties.ColumnSizes, properties.Columns ?? 1).Select(Track)),
            ];
        }

        var common = $"id=\"{name}\" style=\"{string.Join(";", style)}\"";

        switch (control.Type)
        {
            case ControlType.Label:
                markup.AppendLine($"{indent}<span {common} class=\"uib-label\">{Text(properties.Text)}</span>");
                break;
            case ControlType.Button:
                markup.AppendLine($"{indent}<button {common} type=\"button\" class=\"uib-button\" @onclick=\"{HandlerName(control)}\">{Text(properties.Text)}</button>");
                break;
            case ControlType.TextBox when properties.IsMultiline == true:
                markup.AppendLine($"{indent}<textarea {common} class=\"uib-input\" @bind=\"{name}\" @bind:event=\"oninput\" @bind:after=\"{HandlerName(control)}\"></textarea>");
                break;
            case ControlType.TextBox:
            case ControlType.PasswordBox:
                var inputType = control.Type == ControlType.PasswordBox ? "password" : "text";
                markup.AppendLine($"{indent}<input {common} type=\"{inputType}\" class=\"uib-input\" @bind=\"{name}\" @bind:event=\"oninput\" @bind:after=\"{HandlerName(control)}\" />");
                break;
            case ControlType.CheckBox:
                markup.AppendLine($"{indent}<label {common} class=\"uib-check\"><input type=\"checkbox\" @bind=\"{name}\" @bind:after=\"{HandlerName(control)}\" /><span>{Text(properties.Text)}</span></label>");
                break;
            case ControlType.RadioButton:
                markup.AppendLine($"{indent}<label {common} class=\"uib-check\"><input type=\"radio\" name=\"{group}\" checked=\"@{name}\" @onchange=\"{HandlerName(control)}\" /><span>{Text(properties.Text)}</span></label>");
                break;
            case ControlType.ComboBox:
            case ControlType.ListBox:
                var items = properties.Items ?? [];

                // A ComboBox starts with nothing chosen, as in WPF and WinForms; a list box
                // shows at least two rows so the browser draws it as a list.
                var list = control.Type == ControlType.ListBox ? $" size=\"{Number(Math.Max(2, items.Count))}\"" : "";
                markup.AppendLine($"{indent}<select {common} class=\"uib-input\"{list} @bind=\"{name}\" @bind:after=\"{HandlerName(control)}\">");
                if (control.Type == ControlType.ComboBox)
                {
                    markup.AppendLine($"{indent}    <option value=\"\"></option>");
                }

                foreach (var item in items)
                {
                    markup.AppendLine($"{indent}    <option value=\"{Attribute(item)}\">{Text(item)}</option>");
                }

                markup.AppendLine($"{indent}</select>");
                break;
            case ControlType.Slider:
                markup.AppendLine($"{indent}<input {common} type=\"range\" class=\"uib-range\" min=\"{Number(properties.Minimum ?? 0)}\" max=\"{Number(properties.Maximum ?? 100)}\" step=\"1\" @bind=\"{name}\" @bind:event=\"oninput\" @bind:after=\"{HandlerName(control)}\" />");
                break;
            case ControlType.ProgressBar:
                // HTML progress bars start at zero, so the value is shifted by the minimum.
                var minimum = properties.Minimum ?? 0;
                var span = (properties.Maximum ?? 100) - minimum;
                markup.AppendLine($"{indent}<progress {common} class=\"uib-progress\" max=\"{Number(span)}\" value=\"@({name} - ({Number(minimum)}))\"></progress>");
                break;
            case ControlType.Image when properties.ImageData is not null:
                var fit = properties.Stretch == ImageStretch.Fill ? "fill" : "contain";
                markup.AppendLine($"{indent}<img id=\"{name}\" style=\"{string.Join(";", style)};object-fit:{fit}\" src=\"{ImageFile.ExportPath(screen, control)}\" alt=\"\" class=\"uib-image\" />");
                break;
            case ControlType.Image:
                markup.AppendLine($"{indent}<span {common} class=\"uib-image\"></span>");
                break;
            case ControlType.DatePicker:
                markup.AppendLine($"{indent}<input {common} type=\"date\" class=\"uib-input\" @bind=\"{name}\" @bind:after=\"{HandlerName(control)}\" />");
                break;
            case ControlType.StackPanel:
                markup.AppendLine($"{indent}<div {common} class=\"uib-stack\">");
                AppendStackChildren(markup, screen, control, depth + 1);
                markup.AppendLine($"{indent}</div>");
                break;
            case ControlType.GroupBox:
                var (left, top, right, bottom) = ContainerLayout.GroupBoxInset;
                markup.AppendLine($"{indent}<div {common} class=\"uib-group\">");
                var frameStyle = properties.Background is { } frameFill ? $" style=\"background-color:{frameFill}\"" : "";
                markup.AppendLine($"{indent}    <div class=\"uib-frame\"{frameStyle}></div>");
                var titleRules = StyleRules(properties, background: false).ToList();
                var titleStyle = titleRules.Count > 0 ? $" style=\"{string.Join(";", titleRules)}\"" : "";
                markup.AppendLine($"{indent}    <span class=\"uib-title\"{titleStyle}>{Text(properties.Text)}</span>");
                markup.AppendLine($"{indent}    <div class=\"uib-content\" style=\"left:{Px(left)};top:{Px(top)};right:{Px(right)};bottom:{Px(bottom)};{StackStyle(properties)}\">");
                AppendStackChildren(markup, screen, control, depth + 2);
                markup.AppendLine($"{indent}    </div>");
                markup.AppendLine($"{indent}</div>");
                break;
            case ControlType.TabControl:
                // A row of tabs over a frame, and each page at the fixed inset; the field named
                // after the TabControl is the index of the page shown.
                markup.AppendLine($"{indent}<div {common} class=\"uib-tabs\">");
                markup.AppendLine($"{indent}    <div class=\"uib-tabstrip\">");
                var pages = control.Children ?? [];
                for (var i = 0; i < pages.Count; i++)
                {
                    markup.AppendLine($"{indent}        <button type=\"button\" class=\"uib-tab@({name} == {Number(i)} ? \" uib-tab-selected\" : \"\")\" @onclick=\"() => {HandlerName(control)}({Number(i)})\">{Text(pages[i].Properties.Text)}</button>");
                }

                markup.AppendLine($"{indent}    </div>");
                markup.AppendLine($"{indent}    <div class=\"uib-tabframe\"></div>");
                var (tabLeft, tabTop, tabRight, tabBottom) = ContainerLayout.TabControlInset;
                for (var i = 0; i < pages.Count; i++)
                {
                    var pageStyle = new List<string> { $"left:{Px(tabLeft)}", $"top:{Px(tabTop)}", $"right:{Px(tabRight)}", $"bottom:{Px(tabBottom)}" };
                    AppendElement(markup, screen, pages[i], pageStyle, pages[i].Name, depth + 1, hiddenUnless: $"{name} != {Number(i)}");
                }

                markup.AppendLine($"{indent}</div>");
                break;
            case ControlType.TabPage:
                markup.AppendLine($"{indent}<div {common} class=\"uib-content\" hidden=\"@({hiddenUnless})\">");
                AppendStackChildren(markup, screen, control, depth + 1);
                markup.AppendLine($"{indent}</div>");
                break;
            case ControlType.Grid:
                markup.AppendLine($"{indent}<div {common} class=\"uib-grid\">");
                foreach (var child in control.Children ?? [])
                {
                    var cell = new List<string>
                    {
                        $"grid-row:{Number((child.Row ?? 0) + 1)} / span {Number(child.RowSpan ?? 1)}",
                        $"grid-column:{Number((child.Column ?? 0) + 1)} / span {Number(child.ColumnSpan ?? 1)}",
                    };
                    AppendElement(markup, screen, child, cell, control.Name, depth + 1);
                }

                markup.AppendLine($"{indent}</div>");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type.");
        }
    }

    private static IEnumerable<string> StyleRules(ControlProperties properties, bool background)
    {
        if (properties.FontSize is { } size)
        {
            yield return $"font-size:{Px(size)}";
        }

        if (properties.IsBold == true)
        {
            yield return "font-weight:bold";
        }

        if (properties.Foreground is { } text)
        {
            yield return $"color:{text}";
        }

        if (background && properties.Background is { } fill)
        {
            yield return $"background-color:{fill}";
        }
    }

    private static string StackStyle(ControlProperties properties) =>
        properties.Orientation == StackOrientation.Horizontal ? "flex-direction:row" : "flex-direction:column";

    /// <summary>
    /// A StackPanel's or GroupBox's children: each keeps its size along the stack, stretches
    /// across it, and has the spacing as a leading margin.
    /// </summary>
    private static void AppendStackChildren(StringBuilder markup, ScreenDocument screen, ControlDocument stack, int depth)
    {
        var properties = stack.Properties;
        var children = stack.Children ?? [];
        var vertical = properties.Orientation != StackOrientation.Horizontal;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var gap = i > 0 ? properties.Spacing ?? 0 : 0;
            var style = vertical
                ? new List<string> { $"height:{Px(child.Height)}", "width:100%" }
                : new List<string> { $"width:{Px(child.Width)}", "height:100%" };
            if (gap > 0)
            {
                style.Add(vertical ? $"margin-top:{Px(gap)}" : $"margin-left:{Px(gap)}");
            }

            AppendElement(markup, screen, child, style, stack.Name, depth);
        }
    }

    /// <summary>
    /// The regenerated code behind a page: a field for each control's value, the handlers the
    /// markup refers to, and a partial method (hook) per control that the developer may
    /// implement. Unimplemented hooks compile away.
    /// </summary>
    public static string EventsCode(ProjectDocument document, string rootNamespace) =>
        EventsCode(document, document.MainScreen, rootNamespace);

    public static string EventsCode(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        var className = ClassName(document, screen);
        var all = ControlTree.All(screen.Controls).ToList();
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {className}.razor.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClick() { Console.WriteLine(NameTextBox); }");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine("using Microsoft.AspNetCore.Components;");
        code.AppendLine("using Microsoft.JSInterop;");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace}{PagesNamespaceSuffix};");
        code.AppendLine();
        code.AppendLine($"public partial class {className}");
        code.AppendLine("{");
        code.AppendLine("    [Inject]");
        code.AppendLine("    private NavigationManager Navigation { get; set; } = default!;");
        code.AppendLine();
        code.AppendLine("    [Inject]");
        code.AppendLine("    private IJSRuntime JS { get; set; } = default!;");

        var fields = all.Select(c => (Control: c, Field: ValueField(c))).Where(f => f.Field is not null).ToList();
        if (fields.Count > 0)
        {
            code.AppendLine();
            code.AppendLine("    // What each control shows, starting from the design. Read or change them in your code.");
            foreach (var (control, field) in fields)
            {
                code.AppendLine($"    private {field!.Value.Type} {control.Name} = {field.Value.Initial};");
            }
        }

        foreach (var control in all.Where(c => EventFor(c.Type) is not null))
        {
            code.AppendLine();
            if (control.Type == ControlType.TabControl)
            {
                // Choosing a tab shows its page.
                code.AppendLine($"    private void {HandlerName(control)}(int index)");
                code.AppendLine("    {");
                code.AppendLine($"        {control.Name} = index;");
                code.AppendLine($"        {HookName(control)}();");
                code.AppendLine("    }");
                continue;
            }

            code.AppendLine($"    private void {HandlerName(control)}()");
            code.AppendLine("    {");

            // A radio button clears the others in its group before its hook runs.
            if (control.Type == ControlType.RadioButton)
            {
                code.AppendLine($"        {control.Name} = true;");
                foreach (var other in RadioGroup(screen, control).Where(r => r.Id != control.Id))
                {
                    code.AppendLine($"        {other.Name} = false;");
                }
            }

            code.AppendLine($"        {HookName(control)}();");
            if (control.Properties.OpensScreen is { } id && document.FindScreen(id) is { } target)
            {
                code.AppendLine($"        Navigation.NavigateTo({Literal(Route(document, target))});");
            }
            else if (control.Properties.ClosesScreen == true)
            {
                // A web page cannot close itself; going back returns to the page that opened it.
                code.AppendLine("        _ = JS.InvokeVoidAsync(\"history.back\");");
            }

            code.AppendLine("    }");
        }

        foreach (var control in all.Where(c => EventFor(c.Type) is not null))
        {
            code.AppendLine();
            code.AppendLine($"    /// <summary>{control.Type} \"{control.Name}\": {EventFor(control.Type)}.</summary>");
            code.AppendLine($"    partial void {HookName(control)}();");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    /// <summary>The RadioButtons beside a RadioButton: in the same container, or on the screen.</summary>
    private static IEnumerable<ControlDocument> RadioGroup(ScreenDocument screen, ControlDocument radio)
    {
        var siblings = ControlTree.ParentOf(screen.Controls, radio.Id)?.Children ?? screen.Controls;
        return siblings.Where(c => c.Type == ControlType.RadioButton);
    }

    private static string PageCode(string rootNamespace, string className) => $$"""
        namespace {{rootNamespace}}{{PagesNamespaceSuffix}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The page's markup is in {{className}}.razor and its control values and event wiring in
        // {{className}}.Events.g.cs; both are regenerated on every export. Each control's value
        // is a field named after it. To respond to a control, implement its hook, for example:
        //     partial void OnSubmitButtonClick() => Console.WriteLine($"Submitted {NameTextBox}");
        public partial class {{className}}
        {
        }

        """;

    private static string ProjectFileText(string rootNamespace) => $$"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{rootNamespace}}</RootNamespace>
            <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
          </PropertyGroup>

        </Project>

        """;

    private static string ProgramCode(string rootNamespace) => $$"""
        using {{rootNamespace}}.Components;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();

        """;

    private static string AppRazor() => """
        <!DOCTYPE html>
        <html lang="en">

        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <base href="/" />
            <link rel="stylesheet" href="@Assets["uib.css"]" />
            <link rel="stylesheet" href="@Assets["app.css"]" />
            <HeadOutlet @rendermode="InteractiveServer" />
        </head>

        <body>
            <Routes @rendermode="InteractiveServer" />
            <script src="@Assets["_framework/blazor.web.js"]"></script>
        </body>

        </html>

        """;

    private static string RoutesRazor() => """
        <Router AppAssembly="typeof(Program).Assembly">
            <Found Context="routeData">
                <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
            </Found>
        </Router>

        """;

    private static string ImportsRazor(string rootNamespace) => $$"""
        @using System.Net.Http
        @using Microsoft.AspNetCore.Components.Forms
        @using Microsoft.AspNetCore.Components.Routing
        @using Microsoft.AspNetCore.Components.Web
        @using static Microsoft.AspNetCore.Components.Web.RenderMode
        @using Microsoft.JSInterop
        @using {{rootNamespace}}
        @using {{rootNamespace}}.Components

        """;

    private static string LayoutRazor() => """
        @inherits LayoutComponentBase

        @Body

        """;

    private static string AppCss() => """
        /* Created once by Standalone UI Builder: your own styles go here. The layout rules the
           builder relies on are in uib.css, which is regenerated on every export. */

        """;

    /// <summary>
    /// The builder's styles. Controls use their exact designed boxes (border-box sizing), in the
    /// same font size the designer uses.
    /// </summary>
    private static string BuilderCss() => $$"""
        /* {{ProjectExporter.GeneratedMarker}}. This file is replaced on every export; put your own
           styles in app.css. */
        html, body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; font-size: 12px; }
        *, *::before, *::after { box-sizing: border-box; }
        .uib-screen { position: relative; overflow: hidden; }
        .uib-screen * { margin: 0; min-width: 0; min-height: 0; font: inherit; }
        .uib-label { display: flex; align-items: center; padding: 0 2px; white-space: nowrap; overflow: hidden; }
        .uib-check { display: flex; align-items: center; gap: 4px; white-space: nowrap; overflow: hidden; }
        .uib-check > input { margin: 0; }
        .uib-input, .uib-button, .uib-range, .uib-progress, .uib-image { display: block; }
        textarea.uib-input { resize: none; }
        .uib-stack, .uib-content { display: flex; overflow: hidden; }
        .uib-stack > *, .uib-content > * { flex: none; }
        .uib-grid { display: grid; overflow: hidden; }
        .uib-grid > * { width: 100%; height: 100%; }
        .uib-group { position: relative; }
        .uib-frame { position: absolute; left: 0; right: 0; top: 8px; bottom: 0; border: 1px solid #d5dfe5; border-radius: 3px; }
        .uib-title { position: absolute; left: 6px; top: 0; padding: 0 3px; line-height: 16px; background: #fff; white-space: nowrap; }
        .uib-content { position: absolute; }
        .uib-content[hidden] { display: none; }
        .uib-tabs { position: relative; }
        .uib-tabstrip { position: absolute; left: 0; right: 0; top: 0; height: 28px; display: flex; gap: 2px; overflow: hidden; }
        .uib-tab { flex: none; height: 28px; padding: 0 10px; border: 1px solid #d5dfe5; border-bottom: none; border-radius: 3px 3px 0 0; background: #f0f0f0; white-space: nowrap; cursor: pointer; }
        .uib-tab-selected { background: #fff; }
        .uib-tabframe { position: absolute; left: 0; right: 0; top: 28px; bottom: 0; border: 1px solid #d5dfe5; background: #fff; }

        """;

    private static string Track(GridTrackSize size) =>
        size.IsProportional
            ? size.Value.ToString("0.####", CultureInfo.InvariantCulture) + "fr"
            : Px((int)size.Value);

    private static string Px(int value) => value.ToString(CultureInfo.InvariantCulture) + "px";

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

    /// <summary>
    /// Text as Razor markup: HTML-escaped, with "@" doubled so Razor shows it rather than
    /// reading code.
    /// </summary>
    private static string Text(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? "").Replace("@", "@@", StringComparison.Ordinal);

    private static string Attribute(string value) => Text(value);

    // "*@" would end a Razor comment early.
    private static string Comment(string value) => value.Replace("*@", "* @", StringComparison.Ordinal);
}
