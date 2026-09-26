using System.Globalization;
using System.Text;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output.WinForms;

/// <summary>
/// Generates a complete WinForms application from a builder project: a project file, Program,
/// and a form for each screen whose Designer file creates every control with its designed
/// position, size, anchors, text and values. The Designer file follows the shape Visual Studio
/// writes. The first screen is MainForm, which Program runs; the others are named after
/// themselves (SettingsForm) for the developer's code to open.
/// Output is deterministic: the same document always produces the same text.
/// </summary>
public static class WinFormsGenerator
{
    /// <summary>The first screen's form, which Program runs.</summary>
    public const string FormClassName = "MainForm";

    private const string ClassSuffix = "Form";

    // Form members the generated code assigns or calls through "this."; a control field with one
    // of these names would hide it and break the build. The form's own class name is checked
    // separately.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "InitializeComponent", "ViewModel", "components", "Dispose", "Controls", "Text", "Name",
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
        ControlType.RadioButton => "Click",
        ControlType.ListBox => "SelectedIndexChanged",
        ControlType.Slider => "ValueChanged",
        ControlType.DatePicker => "ValueChanged",
        ControlType.PasswordBox => "TextChanged",
        ControlType.TabControl => "SelectedIndexChanged",
        _ => null,
    };

    public static string HandlerName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"{control.Name}_{e}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    public static string HookName(ControlDocument control) =>
        EventFor(control.Type) is { } e ? $"On{control.Name}{e}" : throw new ArgumentException($"A {control.Type} has no event hook.");

    /// <summary>Returns reasons the document cannot be exported to WinForms as it stands, or an empty list.</summary>
    public static IReadOnlyList<string> Check(ProjectDocument document)
    {
        var problems = CodeNames.CheckScreenClassNames(document, ClassSuffix, "form").ToList();
        foreach (var screen in document.Screens)
        {
            // With one screen, messages read as before; with several, they say which screen.
            var prefix = document.Screens.Count > 1 ? $"Screen \"{screen.Name}\": " : "";
            problems.AddRange(CheckScreen(ClassName(document, screen), screen).Select(p => prefix + p));
        }

        return problems;
    }

    /// <summary>The class a screen's form gets: MainForm for the first screen, otherwise SettingsForm and so on.</summary>
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
                problems.Add($"\"{control.Name}\" is a C# keyword and cannot be used as a control name in WinForms code. Rename it.");
            }
            else if (ReservedNames.Contains(control.Name) || control.Name == className)
            {
                problems.Add($"\"{control.Name}\" clashes with a member of the generated form. Rename the control.");
            }
        }

        // A GroupBox's children sit in a generated layout panel named after it.
        var names = all.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var group in all.Where(c => c.Type == ControlType.GroupBox))
        {
            if (names.Contains(LayoutPanelName(group)))
            {
                problems.Add($"\"{LayoutPanelName(group)}\" clashes with the layout panel generated for GroupBox \"{group.Name}\". Rename one of them.");
            }
        }

        // A TabControl and its pages sit in a generated panel named after it.
        foreach (var tabs in all.Where(c => c.Type == ControlType.TabControl))
        {
            if (names.Contains(HostPanelName(tabs)))
            {
                problems.Add($"\"{HostPanelName(tabs)}\" clashes with the panel generated for TabControl \"{tabs.Name}\". Rename one of them.");
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
        ];

        foreach (var screen in document.Screens)
        {
            var className = ClassName(document, screen);
            files.Add(new($"{className}.cs", FormCode(rootNamespace, className), Regenerate: false));
            files.Add(new($"{className}.Designer.cs", DesignerCode(document, screen, rootNamespace), Regenerate: true));
            files.Add(new($"{className}.Events.g.cs", EventsCode(document, screen, rootNamespace), Regenerate: true));
            if (DataBindings.HasViewModel(screen))
            {
                files.Add(new(ViewModelCode.FileName(document, screen), ViewModel(document, screen, rootNamespace), Regenerate: true));
            }
        }

        files.AddRange(CodeNames.ImageFiles(document));
        return files;
    }

    /// <summary>The first screen's form.</summary>
    public static string DesignerCode(ProjectDocument document, string rootNamespace) =>
        DesignerCode(document, document.MainScreen, rootNamespace);

    public static string DesignerCode(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        // Text on the design's own backgrounds stays readable in a dark theme.
        screen = ThemeContrast.Apply(document.Theme, screen);
        var className = ClassName(document, screen);
        var controls = screen.Controls;
        var all = ControlTree.All(controls).ToList();
        var containers = all.Where(c => c.Children is not null).ToList();
        var resizable = AnchorLayout.IsResizable(screen);
        var code = new StringBuilder();

        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker} from \"{Comment(document.Name)}\". This file is replaced on every export;");
        code.AppendLine($"// change the layout in the builder, and put your own code in {className}.cs.");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable disable");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"partial class {className}");
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
        // The colour mode is set once, before the first form's window exists.
        if (document.Theme != ProjectTheme.Light && screen.Id == document.MainScreen.Id)
        {
            code.AppendLine($"    /// <summary>The project's theme: {(document.Theme == ProjectTheme.Dark ? "dark" : "light or dark, following Windows")}.</summary>");
            code.AppendLine($"    static {className}()");
            code.AppendLine("    {");
            code.AppendLine("#pragma warning disable WFO5001 // Dark mode is experimental in Windows Forms.");
            code.AppendLine($"        System.Windows.Forms.Application.SetColorMode(System.Windows.Forms.SystemColorMode.{document.Theme});");
            code.AppendLine("#pragma warning restore WFO5001");
            code.AppendLine("    }");
            code.AppendLine();
        }

        code.AppendLine("    #region Windows Form Designer generated code");
        code.AppendLine();
        code.AppendLine("    private void InitializeComponent()");
        code.AppendLine("    {");

        foreach (var control in all)
        {
            code.AppendLine($"        this.{control.Name} = new System.Windows.Forms.{WinFormsType(control.Type)}();");
            if (control.Type == ControlType.GroupBox)
            {
                code.AppendLine($"        this.{LayoutPanelName(control)} = new System.Windows.Forms.TableLayoutPanel();");
            }
            else if (control.Type == ControlType.TabControl)
            {
                code.AppendLine($"        this.{HostPanelName(control)} = new System.Windows.Forms.Panel();");
            }
        }

        foreach (var container in containers)
        {
            code.AppendLine($"        this.{container.Name}.SuspendLayout();");
            if (container.Type == ControlType.GroupBox)
            {
                code.AppendLine($"        this.{LayoutPanelName(container)}.SuspendLayout();");
            }
            else if (container.Type == ControlType.TabControl)
            {
                code.AppendLine($"        this.{HostPanelName(container)}.SuspendLayout();");
            }
        }

        code.AppendLine("        this.SuspendLayout();");

        for (var i = 0; i < controls.Count; i++)
        {
            AppendControl(code, screen, controls[i], parent: null, index: i);
        }

        code.AppendLine("        // ");
        code.AppendLine($"        // {className}");
        code.AppendLine("        // ");

        // Sizes are designed in DIPs (1/96 inch); Dpi scaling makes them physical at any DPI.
        code.AppendLine("        this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);");
        code.AppendLine("        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;");
        code.AppendLine($"        this.ClientSize = new System.Drawing.Size({Number(screen.Width)}, {Number(screen.Height)});");

        // WinForms puts the first control added on top, so add them in reverse draw order.
        for (var i = controls.Count - 1; i >= 0; i--)
        {
            code.AppendLine($"        this.Controls.Add(this.{OuterName(controls[i])});");
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

        code.AppendLine($"        this.Name = \"{className}\";");

        // The first screen's form shows the project name; other screens show their own name.
        code.AppendLine($"        this.Text = {Literal(screen.Id == document.MainScreen.Id ? document.Name : screen.Name)};");
        for (var i = containers.Count - 1; i >= 0; i--)
        {
            if (containers[i].Type == ControlType.GroupBox)
            {
                code.AppendLine($"        this.{LayoutPanelName(containers[i])}.ResumeLayout(false);");
            }

            code.AppendLine($"        this.{containers[i].Name}.ResumeLayout(false);");
            if (containers[i].Type == ControlType.TabControl)
            {
                code.AppendLine($"        this.{HostPanelName(containers[i])}.ResumeLayout(false);");
            }
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
            if (control.Type == ControlType.GroupBox)
            {
                code.AppendLine($"    private System.Windows.Forms.TableLayoutPanel {LayoutPanelName(control)};");
            }
            else if (control.Type == ControlType.TabControl)
            {
                code.AppendLine($"    private System.Windows.Forms.Panel {HostPanelName(control)};");
            }
        }

        code.AppendLine("}");
        return code.ToString();
    }

    /// <summary>
    /// The WinForms control for each type. StackPanel and Grid become a TableLayoutPanel set
    /// up to follow the builder's layout rules; a GroupBox holds one too.
    /// </summary>
    private static string WinFormsType(ControlType type) => type switch
    {
        ControlType.StackPanel or ControlType.Grid or ControlType.TabPage => "TableLayoutPanel",
        ControlType.Slider => "TrackBar",
        ControlType.DatePicker => "DateTimePicker",
        ControlType.PasswordBox => "TextBox",
        ControlType.Image => "PictureBox",
        _ => type.ToString(),
    };

    private static string ColorCode(string color)
    {
        var (red, green, blue) = ControlColor.Parts(color);
        return $"System.Drawing.Color.FromArgb({Number(red)}, {Number(green)}, {Number(blue)})";
    }

    /// <summary>
    /// A control's TabIndex: its place among the controls beside it. WinForms orders Tab within
    /// each container, so with a tab order set on the screen, a container's controls are
    /// visited together, where the first of them comes (see <see cref="TabSequence.SiblingRanks"/>).
    /// </summary>
    private static int TabRank(ScreenDocument screen, ControlDocument control, int index) =>
        screen.TabOrder is not null && TabSequence.SiblingRanks(screen).TryGetValue(control.Id, out var rank) ? rank : index;

    /// <summary>The TableLayoutPanel inside a GroupBox that lines up its children.</summary>
    public static string LayoutPanelName(ControlDocument group) => group.Name + "Layout";

    /// <summary>The Panel in a TabControl's place that holds the TabControl and, on top, its pages.</summary>
    public static string HostPanelName(ControlDocument tabs) => tabs.Name + "Host";

    /// <summary>The control a container adds: the host panel for a TabControl, otherwise the control.</summary>
    private static string OuterName(ControlDocument control) =>
        control.Type == ControlType.TabControl ? HostPanelName(control) : control.Name;

    /// <summary>
    /// Writes one control's settings, then its children's. A control on the form has a location,
    /// size and anchor; one in a container fills its table cell (Dock = Fill), and a stack's
    /// spacing becomes the child's leading margin inside a cell sized to hold both.
    /// </summary>
    private static void AppendControl(StringBuilder code, ScreenDocument screen, ControlDocument control, ControlDocument? parent, int index)
    {
        if (control.Type == ControlType.TabControl)
        {
            AppendTabControl(code, screen, control, parent, index);
            return;
        }

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
        else if (parent.Type == ControlType.TabControl)
        {
            // A page lies over the TabControl, anchored at the fixed inset in its host panel;
            // only the page whose tab is chosen is visible.
            var (left, top, right, bottom) = ContainerLayout.TabControlInset;
            Set("Anchor", AnchorStyles(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom));
            Set("Location", $"new System.Drawing.Point({Number(left)}, {Number(top)})");
            Set("Size", $"new System.Drawing.Size({Number(parent.Width - left - right)}, {Number(parent.Height - top - bottom)})");
            Set("Visible", index == ContainerLayout.ShownTab(parent) ? "true" : "false");
            if (properties.Background is null)
            {
                Set("BackColor", "System.Drawing.SystemColors.Window");
            }
        }
        else
        {
            var gap = ControlCatalog.Get(parent.Type).IsStack && index > 0 ? parent.Properties.Spacing ?? 0 : 0;
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
            case ControlType.RadioButton:
                Set("AutoSize", "false");
                Set("Checked", properties.IsChecked == true ? "true" : "false");
                break;
            case ControlType.ComboBox:
                Set("DropDownStyle", "System.Windows.Forms.ComboBoxStyle.DropDownList");
                AddItems();
                break;
            case ControlType.ListBox:
                // Otherwise the list shrinks to a whole number of items.
                Set("IntegralHeight", "false");
                AddItems();
                break;
            case ControlType.TextBox when properties.IsMultiline == true:
                Set("Multiline", "true");
                Set("ScrollBars", "System.Windows.Forms.ScrollBars.Vertical");
                break;
            case ControlType.PasswordBox:
                Set("UseSystemPasswordChar", "true");
                break;
            case ControlType.Slider:
                // A TrackBar sizes itself unless told not to; ticks off, like WPF's Slider.
                Set("AutoSize", "false");
                Set("TickStyle", "System.Windows.Forms.TickStyle.None");
                SetRange();
                break;
            case ControlType.ProgressBar:
                SetRange();
                break;
            case ControlType.Image:
                // Zoom keeps the proportions, like WPF's Uniform; the picture is loaded from the
                // Assets folder next to the application.
                Set("SizeMode", properties.Stretch == ImageStretch.Fill
                    ? "System.Windows.Forms.PictureBoxSizeMode.StretchImage"
                    : "System.Windows.Forms.PictureBoxSizeMode.Zoom");
                if (properties.ImageData is not null)
                {
                    var parts = ImageFile.ExportPath(screen, control).Split('/').Select(Literal);
                    Set("Image", $"System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, {string.Join(", ", parts)}))");
                }

                break;
            case ControlType.DatePicker:
                // Unticked: no date chosen yet, as in WPF's DatePicker.
                Set("Format", "System.Windows.Forms.DateTimePickerFormat.Short");
                Set("ShowCheckBox", "true");
                Set("Checked", "false");
                break;
        }

        // Sizes are designed in DIPs; a WinForms font size is in points (3/4 of a DIP), and the
        // WinForms default font is Segoe UI 9 pt, the designer's 12 DIPs.
        if (properties.FontSize is not null || properties.IsBold == true)
        {
            var points = (properties.FontSize ?? ControlDefinition.DefaultFontSize) * 0.75;
            var bold = properties.IsBold == true ? ", System.Drawing.FontStyle.Bold" : "";
            Set("Font", $"new System.Drawing.Font(\"Segoe UI\", {points.ToString("0.##", CultureInfo.InvariantCulture)}F{bold})");
        }

        if (properties.Foreground is { } foreground)
        {
            Set("ForeColor", ColorCode(foreground));
        }

        if (properties.Background is { } background)
        {
            Set("BackColor", ColorCode(background));
        }

        void AddItems()
        {
            if (properties.Items is { Count: > 0 } items)
            {
                code.AppendLine($"        this.{name}.Items.AddRange(new object[] {{ {string.Join(", ", items.Select(Literal))} }});");
            }
        }

        // Minimum first: WinForms raises the maximum to meet it, and rejects a value outside them.
        void SetRange()
        {
            Set("Minimum", Number(properties.Minimum ?? 0));
            Set("Maximum", Number(properties.Maximum ?? 100));
            Set("Value", Number(properties.Value ?? 0));
        }

        if (control.Type == ControlType.GroupBox)
        {
            // The GroupBox has its design size before its layout panel is anchored inside it, so
            // the panel keeps the fixed inset however the GroupBox is later stretched.
            var layoutName = LayoutPanelName(control);
            var (left, top, right, bottom) = ContainerLayout.GroupBoxInset;
            Set("Size", $"new System.Drawing.Size({Number(control.Width)}, {Number(control.Height)})");
            code.AppendLine($"        this.{name}.Controls.Add(this.{layoutName});");
            AppendTable(code, layoutName, control, control.Children ?? []);
            code.AppendLine($"        this.{layoutName}.Anchor = {AnchorStyles(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom)};");
            code.AppendLine($"        this.{layoutName}.Location = new System.Drawing.Point({Number(left)}, {Number(top)});");
            code.AppendLine($"        this.{layoutName}.Name = {Literal(layoutName)};");

            // WinForms controls take their parent's font and text colour; a GroupBox's apply to
            // its title only, as in WPF, so its layout panel goes back to the defaults.
            if (properties.FontSize is not null || properties.IsBold == true)
            {
                code.AppendLine($"        this.{layoutName}.Font = new System.Drawing.Font(\"Segoe UI\", 9F);");
            }

            if (properties.Foreground is not null)
            {
                code.AppendLine($"        this.{layoutName}.ForeColor = System.Drawing.SystemColors.ControlText;");
            }
            code.AppendLine($"        this.{layoutName}.Size = new System.Drawing.Size({Number(control.Width - left - right)}, {Number(control.Height - top - bottom)});");
        }
        else if (control.Children is { } children)
        {
            AppendTable(code, name, control, children);
        }

        if (parent is null)
        {
            Set("Location", $"new System.Drawing.Point({Number(control.X)}, {Number(control.Y)})");
        }

        Set("Name", Literal(name));
        if (parent is null && control.Type != ControlType.GroupBox)
        {
            Set("Size", $"new System.Drawing.Size({Number(control.Width)}, {Number(control.Height)})");
        }

        // A tab page comes after its TabControl's tabs, which have TabIndex 0 in the host panel.
        Set("TabIndex", Number(parent?.Type == ControlType.TabControl ? index + 1 : TabRank(screen, control, index)));

        switch (control.Type)
        {
            case ControlType.Label:
            case ControlType.Button:
            case ControlType.CheckBox:
            case ControlType.RadioButton:
            case ControlType.GroupBox:
                // "&" marks an access key in these controls; the designer shows text literally.
                Set("Text", Literal((properties.Text ?? "").Replace("&", "&&", StringComparison.Ordinal)));
                break;
            case ControlType.TextBox:
                Set("Text", Literal(properties.Text ?? ""));
                break;
        }

        // As in Visual Studio's designer: the visual style's background, unless the design sets
        // one (with the visual style on, a button ignores its BackColor).
        if (control.Type is ControlType.Button or ControlType.CheckBox or ControlType.RadioButton)
        {
            Set("UseVisualStyleBackColor", properties.Background is null ? "true" : "false");
        }

        if (EventFor(control.Type) is { } e)
        {
            code.AppendLine($"        this.{name}.{e} += this.{HandlerName(control)};");
        }

        if (properties.Binding is { } binding && BoundProperty(control.Type) is { } bound)
        {
            var update = ControlCatalog.Get(control.Type).ShowsBindingOnly ? "Never" : "OnPropertyChanged";
            code.AppendLine($"        this.{name}.DataBindings.Add({Literal(bound)}, this.ViewModel, {Literal(binding)}, true, System.Windows.Forms.DataSourceUpdateMode.{update});");
        }

        if (properties.EnabledBinding is { } enabled)
        {
            code.AppendLine($"        this.{name}.DataBindings.Add(\"Enabled\", this.ViewModel, {Literal(enabled)}, true, System.Windows.Forms.DataSourceUpdateMode.Never);");
        }

        for (var i = 0; i < (control.Children?.Count ?? 0); i++)
        {
            AppendControl(code, screen, control.Children![i], control, i);
        }
    }

    /// <summary>
    /// A screen's view model, with Windows Forms' types: a TrackBar's or ProgressBar's value is a
    /// whole number, and a DateTimePicker's date is never empty, so it starts from today.
    /// </summary>
    public static string ViewModel(ProjectDocument document, ScreenDocument screen, string rootNamespace) =>
        ViewModelCode.Generate(document, screen, rootNamespace,
            kind => kind switch
            {
                BindingKind.Text => "string",
                BindingKind.Flag => "bool",
                BindingKind.Number => "int",
                BindingKind.Choice => "string?",
                _ => "System.DateTime",
            },
            property => property.Kind switch
            {
                BindingKind.Text => ViewModelCode.TextLiteral(property),
                BindingKind.Flag => ViewModelCode.FlagLiteral(property),
                BindingKind.Number => ViewModelCode.NumberLiteral(property),
                BindingKind.Date => "System.DateTime.Today",
                _ => null,
            });

    /// <summary>
    /// The control property bound to the view model. A ComboBox's or ListBox's Text is its chosen
    /// item's text; its selection handler writes a new choice to the view model, since neither
    /// control reports a change of Text as it happens.
    /// </summary>
    private static string? BoundProperty(ControlType type) => type switch
    {
        ControlType.Label or ControlType.TextBox or ControlType.PasswordBox or ControlType.ComboBox or ControlType.ListBox => "Text",
        ControlType.CheckBox or ControlType.RadioButton => "Checked",
        ControlType.Slider or ControlType.ProgressBar or ControlType.DatePicker => "Value",
        _ => null,
    };

    /// <summary>
    /// A TabControl: a Panel in its place holding the real TabControl, which fills it and has an
    /// empty TabPage for each page's tab, and on top each page as a TableLayoutPanel at the
    /// fixed <see cref="ContainerLayout.TabControlInset"/>. The generated handler shows the
    /// page whose tab is chosen.
    /// </summary>
    private static void AppendTabControl(StringBuilder code, ScreenDocument screen, ControlDocument tabs, ControlDocument? parent, int index)
    {
        var name = tabs.Name;
        var host = HostPanelName(tabs);
        var pages = tabs.Children ?? [];
        void Host(string property, string value) => code.AppendLine($"        this.{host}.{property} = {value};");
        void Set(string property, string value) => code.AppendLine($"        this.{name}.{property} = {value};");

        code.AppendLine("        // ");
        code.AppendLine($"        // {host}");
        code.AppendLine("        // ");
        if (parent is null)
        {
            Host("Anchor", AnchorStyles(tabs.Anchor));
            Host("Location", $"new System.Drawing.Point({Number(tabs.X)}, {Number(tabs.Y)})");
        }
        else
        {
            var gap = ControlCatalog.Get(parent.Type).IsStack && index > 0 ? parent.Properties.Spacing ?? 0 : 0;
            var vertical = parent.Properties.Orientation != StackOrientation.Horizontal;
            Host("Dock", "System.Windows.Forms.DockStyle.Fill");
            Host("Margin", gap == 0 ? "new System.Windows.Forms.Padding(0)"
                : vertical ? $"new System.Windows.Forms.Padding(0, {Number(gap)}, 0, 0)"
                : $"new System.Windows.Forms.Padding({Number(gap)}, 0, 0, 0)");
        }

        // The panel has its design size before the pages are anchored inside it. WinForms puts
        // the first control added on top, so the pages go in before the TabControl.
        Host("Size", $"new System.Drawing.Size({Number(tabs.Width)}, {Number(tabs.Height)})");
        foreach (var page in pages)
        {
            code.AppendLine($"        this.{host}.Controls.Add(this.{page.Name});");
        }

        code.AppendLine($"        this.{host}.Controls.Add(this.{name});");
        Host("Name", Literal(host));
        Host("TabIndex", Number(TabRank(screen, tabs, index)));

        code.AppendLine("        // ");
        code.AppendLine($"        // {name}");
        code.AppendLine("        // ");
        Set("Dock", "System.Windows.Forms.DockStyle.Fill");
        foreach (var page in pages)
        {
            code.AppendLine($"        this.{name}.TabPages.Add({Literal(page.Properties.Text ?? "")});");
        }

        if (pages.Count > 0)
        {
            Set("SelectedIndex", Number(ContainerLayout.ShownTab(tabs)));
        }

        Set("Name", Literal(name));
        Set("TabIndex", "0");
        code.AppendLine($"        this.{name}.{EventFor(tabs.Type)} += this.{HandlerName(tabs)};");

        for (var i = 0; i < pages.Count; i++)
        {
            AppendControl(code, screen, pages[i], tabs, i);
        }
    }

    private static void AppendTable(StringBuilder code, string name, ControlDocument container, IReadOnlyList<ControlDocument> children)
    {
        var properties = container.Properties;
        void Line(string text) => code.AppendLine($"        this.{name}.{text}");
        Line("Margin = new System.Windows.Forms.Padding(0);");
        Line("Padding = new System.Windows.Forms.Padding(0);");

        if (ControlCatalog.Get(container.Type).IsStack)
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
                Line(vertical ? $"Controls.Add(this.{OuterName(children[i])}, 0, {Number(i)});" : $"Controls.Add(this.{OuterName(children[i])}, {Number(i)}, 0);");
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
            Line($"Controls.Add(this.{OuterName(child)}, {Number(child.Column ?? 0)}, {Number(child.Row ?? 0)});");
            if (child.RowSpan is > 1)
            {
                Line($"SetRowSpan(this.{OuterName(child)}, {Number(child.RowSpan.Value)});");
            }

            if (child.ColumnSpan is > 1)
            {
                Line($"SetColumnSpan(this.{OuterName(child)}, {Number(child.ColumnSpan.Value)});");
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

    public static string EventsCode(ProjectDocument document, string rootNamespace) =>
        EventsCode(document, document.MainScreen, rootNamespace);

    public static string EventsCode(ProjectDocument document, ScreenDocument screen, string rootNamespace)
    {
        var className = ClassName(document, screen);
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated>");
        code.AppendLine($"// {ProjectExporter.GeneratedMarker}. This file is replaced on every export.");
        code.AppendLine($"// To respond to a control, implement its partial method in {className}.cs, for example:");
        code.AppendLine("//     partial void OnSubmitButtonClick(EventArgs e) { MessageBox.Show(\"Submitted\"); }");
        code.AppendLine("// </auto-generated>");
        code.AppendLine("#nullable enable");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine($"partial class {className}");
        code.AppendLine("{");

        var first = true;
        if (DataBindings.HasViewModel(screen))
        {
            code.AppendLine("    /// <summary>The values the form's controls are bound to.</summary>");
            code.AppendLine($"    public {ViewModelCode.ClassName(document, screen)} ViewModel {{ get; }} = new();");
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
                // The pages lie over the TabControl, not in it: show the one whose tab is chosen.
                code.AppendLine($"    private void {HandlerName(control)}(object? sender, System.EventArgs e)");
                code.AppendLine("    {");
                var pages = control.Children ?? [];
                for (var i = 0; i < pages.Count; i++)
                {
                    code.AppendLine($"        this.{pages[i].Name}.Visible = this.{control.Name}.SelectedIndex == {Number(i)};");
                }

                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine("    }");
            }
            else if (control.Properties.Command is not null
                || control.Properties.OpensScreen is { } opens && document.FindScreen(opens) is not null
                || control.Properties.ClosesScreen == true)
            {
                // The hook runs first, then the button's command, then its action from the design.
                code.AppendLine($"    private void {HandlerName(control)}(object? sender, System.EventArgs e)");
                code.AppendLine("    {");
                code.AppendLine($"        {HookName(control)}(e);");
                if (ViewModelCode.CommandStatement(control) is { } command)
                {
                    code.AppendLine($"        {command}");
                }

                if (control.Properties.OpensScreen is { } id && document.FindScreen(id) is { } target)
                {
                    code.AppendLine($"        using var form = new {ClassName(document, target)}();");
                    code.AppendLine("        form.ShowDialog(this);");
                }
                else if (control.Properties.ClosesScreen == true)
                {
                    code.AppendLine("        Close();");
                }

                code.AppendLine("    }");
            }
            else if (control.Type is ControlType.ComboBox or ControlType.ListBox && control.Properties.Binding is not null)
            {
                code.AppendLine($"    private void {HandlerName(control)}(object? sender, System.EventArgs e)");
                code.AppendLine("    {");
                code.AppendLine($"        this.{control.Name}.DataBindings[\"Text\"]?.WriteValue();");
                code.AppendLine($"        {HookName(control)}(e);");
                code.AppendLine("    }");
            }
            else
            {
                code.AppendLine($"    private void {HandlerName(control)}(object? sender, System.EventArgs e) => {HookName(control)}(e);");
            }

            code.AppendLine();
            code.AppendLine($"    /// <summary>{control.Type} \"{control.Name}\": {e}.</summary>");
            code.AppendLine($"    partial void {HookName(control)}(System.EventArgs e);");
        }

        code.AppendLine("}");
        return code.ToString();
    }

    private static string FormCode(string rootNamespace, string className) => $$"""
        namespace {{rootNamespace}};

        // Created once by Standalone UI Builder and never overwritten: add your code here.
        // The controls are created in {{className}}.Designer.cs, which is regenerated on every export.
        // To respond to a control, implement its hook from {{className}}.Events.g.cs, for example:
        //     partial void OnSubmitButtonClick(EventArgs e) { MessageBox.Show("Submitted"); }
        // To show another screen, create its form: using var settings = new SettingsForm(); settings.ShowDialog(this);
        public partial class {{className}} : Form
        {
            public {{className}}()
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

          <ItemGroup>
            <!-- Pictures from the design, copied next to the application. -->
            <None Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>

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
