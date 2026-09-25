using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.WinForms;

/// <summary>
/// Generates a complete WinForms application from a builder project: a project file, Program,
/// and a MainForm whose Designer file creates every control with its designed position, size,
/// anchors, text and values. The Designer file follows the shape Visual Studio writes.
/// Output is deterministic: the same document always produces the same text.
/// </summary>
public static class WinFormsGenerator
{
    public const string FormClassName = "MainForm";

    // Form members the generated code assigns or calls through "this."; a control field with one
    // of these names would hide it and break the build.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        FormClassName, "InitializeComponent", "components", "Dispose", "Controls", "Text", "Name",
        "ClientSize", "AutoScaleDimensions", "AutoScaleMode", "FormBorderStyle", "MaximizeBox",
        "MinimumSize", "SuspendLayout", "ResumeLayout", "PerformLayout", "SizeFromClientSize",
    };

    // Inherited Form members a control is quite likely to be named after ("CancelButton"). The
    // field is declared "new" so hiding them is explicit and the build has no warning. The
    // generated code never uses these members itself.
    private static readonly HashSet<string> HiddenFormMembers = new(StringComparer.Ordinal)
    {
        "AcceptButton", "CancelButton", "Icon", "Owner", "Location", "Size", "Font", "Tag", "Parent",
        "Cursor", "Region", "Visible", "Enabled", "Width", "Height", "Left", "Top", "Right", "Bottom",
        "Padding", "Margin", "Anchor", "Dock", "BackColor", "ForeColor", "Opacity", "TopMost",
        "WindowState", "StartPosition", "ShowIcon", "ShowInTaskbar", "Handle", "Focused", "Created",
    };

    /// <summary>The one event per control type that gets a hook.</summary>
    private static string? EventFor(ControlType type) => type switch
    {
        ControlType.Button => "Click",
        ControlType.CheckBox => "Click",
        ControlType.TextBox => "TextChanged",
        ControlType.ComboBox => "SelectedIndexChanged",
        _ => null,
    };

    public static string HandlerName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"{control.Name}_{e}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    public static string HookName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"On{control.Name}{e}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    /// <summary>Returns reasons the document cannot be exported to WinForms as it stands, or an empty list.</summary>
    public static IReadOnlyList<string> Check(ProjectDocument document)
    {
        var problems = new List<string>();
        var all = ControlTree.All(document.Screen.Controls).ToList();
        foreach (var control in all)
        {
            if (CodeNames.CSharpKeywords.Contains(control.Name))
            {
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in WinForms code. Rename it.");
            }
            else if (ReservedNames.Contains(control.Name))
            {
                problems.Add($"\"{control.Name}\" clashes with a member of the generated form. Rename the control.");
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

    public static IReadOnlyList<GeneratedFile> Generate(ProjectDocument document, string rootNamespace) =>
    [
        new($"{rootNamespace}.csproj", ProjectFileText(rootNamespace), Regenerate: false),
        new("Program.cs", ProgramCode(rootNamespace), Regenerate: false),
        new($"{FormClassName}.cs", FormCode(rootNamespace), Regenerate: false),
        new($"{FormClassName}.Designer.cs", DesignerCode(document, rootNamespace), Regenerate: true),
        new($"{FormClassName}.Events.g.cs", EventsCode(document, rootNamespace), Regenerate: true),
    ];

    public static string DesignerCode(ProjectDocument document, string rootNamespace)
    {
        var screen = document.Screen;
        var controls = screen.Controls;
        var all = ControlTree.All(controls).ToList();
        var containers = all.Where(c => c.Children is not null).ToList();
        var resizable = AnchorLayout.IsResizable(screen);
        var code = new StringBuilder();

        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        code.AppendLine($"// change the layout in the builder, and put your own code in {FormClassName}.cs.");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable disable");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"partial class {FormClassName}");
        code.AppendLine("{");
        code.AppendLine("    private System.ComponentModel.IContainer components = null;");
        code.AppendLine();
        code.AppendLine("    protected override void Dispose(bool disposing)");
        code.AppendLine("    {");
        code.AppendLine("        if (disposing && (components != null))");
        code.AppendLine("        {");
        code.AppendLine("            components.Dispose();");
        code.AppendLine("        }");
        code.AppendLine();
        code.AppendLine("        base.Dispose(disposing);");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    #region Windows Form Designer generated code");
        code.AppendLine();
        code.AppendLine("    private void InitializeComponent()");
        code.AppendLine("    {");

        foreach (var control in all)
        {
            code.AppendLine($"        this.{control.Name} = new System.Windows.Forms.{WinFormsType(control.Type)}();");
        }

        foreach (var container in containers)
        {
            code.AppendLine($"        this.{container.Name}.SuspendLayout();");
        }

        code.AppendLine("        this.SuspendLayout();");

        for (var i = 0; i < controls.Count; i++)
        {
            AppendControl(code, controls[i], parent: null, index: i);
        }

        code.AppendLine("        // ");
        code.AppendLine($"        // {FormClassName}");
        code.AppendLine("        // ");

        // Sizes are designed in DIPs (1/96 inch); Dpi scaling makes them physical at any DPI.
        code.AppendLine("        this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);");
        code.AppendLine("        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;");
        code.AppendLine($"        this.ClientSize = new System.Drawing.Size({Number(screen.Width)}, {Number(screen.Height)});");

        // WinForms puts the first control added on top, so add them in reverse draw order.
        for (var i = controls.Count - 1; i >= 0; i--)
        {
            code.AppendLine($"        this.Controls.Add(this.{controls[i].Name});");
        }

        if (resizable)
        {
            code.AppendLine("        this.MinimumSize = this.SizeFromClientSize(this.ClientSize);");
        }
        else
        {
            code.AppendLine("        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;");
            code.AppendLine("        this.MaximizeBox = false;");
        }

        code.AppendLine($"        this.Name = \"{FormClassName}\";");
        code.AppendLine($"        this.Text = {Literal(document.Name)};");
        for (var i = containers.Count - 1; i >= 0; i--)
        {
            code.AppendLine($"        this.{containers[i].Name}.ResumeLayout(false);");
        }

        code.AppendLine("        this.ResumeLayout(false);");
        code.AppendLine("        this.PerformLayout();");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    #endregion");

        if (all.Count > 0)
        {
            code.AppendLine();
        }

        foreach (var control in all)
        {
            var hides = HiddenFormMembers.Contains(control.Name) ? "new " : "";
            code.AppendLine($"    private {hides}System.Windows.Forms.{WinFormsType(control.Type)} {control.Name};");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    /// <summary>
    /// Containers become a TableLayoutPanel, set up so it follows the builder's layout rules.
    /// </summary>
    private static string WinFormsType(ControlType type) =>
        type is ControlType.StackPanel or ControlType.Grid ? "TableLayoutPanel" : type.ToString();

    /// <summary>
    /// Writes one control's settings, then its children's. A control on the form has a location,
    /// size and anchor; one in a container fills its table cell (Dock = Fill), and a stack's
    /// spacing becomes the child's leading margin inside a cell sized to hold both.
    /// </summary>
    private static void AppendControl(StringBuilder code, ControlDocument control, ControlDocument? parent, int index)
    {
        var name = control.Name;
        var properties = control.Properties;
        void Set(string property, string value) => code.AppendLine($"        this.{name}.{property} = {value};");

        code.AppendLine("        // ");
        code.AppendLine($"        // {name}");
        code.AppendLine("        // ");
        if (parent is null)
        {
            Set("Anchor", AnchorStyles(control.Anchor));
        }
        else
        {
            var gap = parent.Type == ControlType.StackPanel && index > 0 ? parent.Properties.Spacing ?? 0 : 0;
            var vertical = parent.Properties.Orientation != StackOrientation.Horizontal;
            Set("Dock", "System.Windows.Forms.DockStyle.Fill");
            Set("Margin", gap == 0 ? "new System.Windows.Forms.Padding(0)"
                : vertical ? $"new System.Windows.Forms.Padding(0, {Number(gap)}, 0, 0)"
                : $"new System.Windows.Forms.Padding({Number(gap)}, 0, 0, 0)");
        }

        switch (control.Type)
        {
            case ControlType.Label:
                Set("AutoSize", "false");
                Set("TextAlign", "System.Drawing.ContentAlignment.MiddleLeft");
                break;
            case ControlType.CheckBox:
                Set("AutoSize", "false");
                Set("Checked", properties.IsChecked == true ? "true" : "false");
                break;
            case ControlType.ComboBox:
                Set("DropDownStyle", "System.Windows.Forms.ComboBoxStyle.DropDownList");
                if (properties.Items is { Count: > 0 } items)
                {
                    code.AppendLine($"        this.{name}.Items.AddRange(new object[] {{ {string.Join(", ", items.Select(Literal))} }});");
                }

                break;
        }

        if (control.Children is { } children)
        {
            AppendTable(code, control, children);
        }

        if (parent is null)
        {
            Set("Location", $"new System.Drawing.Point({Number(control.X)}, {Number(control.Y)})");
        }

        Set("Name", Literal(name));
        if (parent is null)
        {
            Set("Size", $"new System.Drawing.Size({Number(control.Width)}, {Number(control.Height)})");
        }

        Set("TabIndex", Number(index));

        switch (control.Type)
        {
            case ControlType.Label:
            case ControlType.Button:
            case ControlType.CheckBox:
                // "&" marks an access key in these controls; the designer shows text literally.
                Set("Text", Literal((properties.Text ?? "").Replace("&", "&&", StringComparison.Ordinal)));
                break;
            case ControlType.TextBox:
                Set("Text", Literal(properties.Text ?? ""));
                break;
        }

        if (control.Type is ControlType.Button or ControlType.CheckBox)
        {
            Set("UseVisualStyleBackColor", "true");
        }

        if (EventFor(control.Type) is { } e)
        {
            code.AppendLine($"        this.{name}.{e} += this.{HandlerName(control)};");
        }

        for (var i = 0; i < (control.Children?.Count ?? 0); i++)
        {
            AppendControl(code, control.Children![i], control, i);
        }
    }

    private static void AppendTable(StringBuilder code, ControlDocument container, IReadOnlyList<ControlDocument> children)
    {
        var name = container.Name;
        var properties = container.Properties;
        void Line(string text) => code.AppendLine($"        this.{name}.{text}");
        Line("Margin = new System.Windows.Forms.Padding(0);");
        Line("Padding = new System.Windows.Forms.Padding(0);");

        if (container.Type == ControlType.StackPanel)
        {
            // One fixed-size row (or column) per child, holding the child and the gap before
            // it, and a last one that takes up whatever space is left.
            var vertical = properties.Orientation != StackOrientation.Horizontal;
            var (along, across) = vertical ? ("Row", "Column") : ("Column", "Row");
            Line($"{across}Count = 1;");
            Line($"{across}Styles.Add(new System.Windows.Forms.{across}Style(System.Windows.Forms.SizeType.Percent, 100F));");
            Line($"{along}Count = {Number(children.Count + 1)};");
            for (var i = 0; i < children.Count; i++)
            {
                var gap = i > 0 ? properties.Spacing ?? 0 : 0;
                var size = (vertical ? children[i].Height : children[i].Width) + gap;
                Line($"{along}Styles.Add(new System.Windows.Forms.{along}Style(System.Windows.Forms.SizeType.Absolute, {Number(size)}F));");
            }

            Line($"{along}Styles.Add(new System.Windows.Forms.{along}Style(System.Windows.Forms.SizeType.Percent, 100F));");
            for (var i = 0; i < children.Count; i++)
            {
                Line(vertical ? $"Controls.Add(this.{children[i].Name}, 0, {Number(i)});" : $"Controls.Add(this.{children[i].Name}, {Number(i)}, 0);");
            }

            return;
        }

        // Fixed rows and columns are Absolute; shares become percentages of the space left.
        var rows = properties.Rows ?? 1;
        var columns = properties.Columns ?? 1;
        Line($"ColumnCount = {Number(columns)};");
        foreach (var style in TrackStyles(GridTrackSize.Resolve(properties.ColumnSizes, columns)))
        {
            Line($"ColumnStyles.Add(new System.Windows.Forms.ColumnStyle({style}));");
        }

        Line($"RowCount = {Number(rows)};");
        foreach (var style in TrackStyles(GridTrackSize.Resolve(properties.RowSizes, rows)))
        {
            Line($"RowStyles.Add(new System.Windows.Forms.RowStyle({style}));");
        }

        foreach (var child in children)
        {
            Line($"Controls.Add(this.{child.Name}, {Number(child.Column ?? 0)}, {Number(child.Row ?? 0)});");
            if (child.RowSpan is > 1)
            {
                Line($"SetRowSpan(this.{child.Name}, {Number(child.RowSpan.Value)});");
            }

            if (child.ColumnSpan is > 1)
            {
                Line($"SetColumnSpan(this.{child.Name}, {Number(child.ColumnSpan.Value)});");
            }
        }
    }

    private static IEnumerable<string> TrackStyles(IReadOnlyList<GridTrackSize> sizes)
    {
        var weights = sizes.Where(s => s.IsProportional).Sum(s => s.Value);
        foreach (var size in sizes)
        {
            yield return size.IsProportional
                ? $"System.Windows.Forms.SizeType.Percent, {(100 * size.Value / weights).ToString("0.####", CultureInfo.InvariantCulture)}F"
                : $"System.Windows.Forms.SizeType.Absolute, {Number((int)size.Value)}F";
        }
    }

    public static string EventsCode(ProjectDocument document, string rootNamespace)
    {
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {FormClassName}.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClick(EventArgs e) { MessageBox.Show(\"Submitted\"); }");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"partial class {FormClassName}");
        code.AppendLine("{");

        var first = true;
        foreach (var control in ControlTree.All(document.Screen.Controls))
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
            code.AppendLine($"    private void {HandlerName(control)}(object? sender, System.EventArgs e) => {HookName(control)}(e);");
            code.AppendLine();
            code.AppendLine($"    /// <summary>{control.Type} \"{control.Name}\": {e}.</summary>");
            code.AppendLine($"    partial void {HookName(control)}(System.EventArgs e);");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    private static string FormCode(string rootNamespace) => $$"""
        namespace {{rootNamespace}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The controls are created in {{FormClassName}}.Designer.cs, which is regenerated on every export.
        // To respond to a control, implement its hook from {{FormClassName}}.Events.g.cs, for example:
        //     partial void OnSubmitButtonClick(EventArgs e) { MessageBox.Show("Submitted"); }
        public partial class {{FormClassName}} : Form
        {
            public {{FormClassName}}()
            {
                InitializeComponent();
            }
        }

        """;

    private static string ProgramCode(string rootNamespace) => $$"""
        namespace {{rootNamespace}};

        internal static class Program
        {
            [STAThread]
            private static void Main()
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new {{FormClassName}}());
            }
        }

        """;

    private static string ProjectFileText(string rootNamespace) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <UseWindowsForms>true</UseWindowsForms>
            <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
            <RootNamespace>{{rootNamespace}}</RootNamespace>
          </PropertyGroup>

        </Project>

        """;

    private static string AnchorStyles(AnchorEdges anchor)
    {
        var parts = new List<string>();
        foreach (var (edge, name) in new[] { (AnchorEdges.Top, "Top"), (AnchorEdges.Bottom, "Bottom"), (AnchorEdges.Left, "Left"), (AnchorEdges.Right, "Right") })
        {
            if (anchor.HasFlag(edge))
            {
                parts.Add($"System.Windows.Forms.AnchorStyles.{name}");
            }
        }

        return parts.Count == 1 ? parts[0] : "(" + string.Join(" | ", parts) + ")";
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A C# string literal for any text.</summary>
    public static string Literal(string value)
    {
        var literal = new StringBuilder("\"");
        foreach (var c in value)
        {
            literal.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) || char.IsSurrogate(c) || c == (char)0x2028 || c == (char)0x2029 => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }

        return literal.Append('"').ToString();
    }

    // A line comment ends at a line break, so keep the name on one line.
    private static string Comment(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
