using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Import.Wpf;

/// <summary>What an import produced, and what it had to leave out or change, in plain language.</summary>
public sealed record ImportResult(ProjectDocument Document, IReadOnlyList<string> Warnings);

/// <summary>An import could not produce a project. The message is suitable for the user.</summary>
public sealed class ImportException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Reads WPF windows (XAML) into a new builder project, one screen per window. It understands
/// what the builder can represent: its control types and containers, positions from a Grid or
/// Canvas (margins and alignment become anchors), text, values, items, fonts, colours and
/// pictures. Anything else is left out and reported. Windows exported by the builder come back
/// as they were designed, including buttons that open or close screens, which are read from
/// the generated <c>.Events.g.cs</c> file beside each window.
/// </summary>
public static partial class WpfImporter
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    // A handler's body may hold one level of braces: new SettingsWindow { Owner = this }.
    [GeneratedRegex(@"private void (\w+)_Click\([^)]*\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}")]
    private static partial Regex ClickHandler();

    [GeneratedRegex(@"new (\w+)\s*(\{[^}]*\})?\s*\.\s*ShowDialog\(|new (\w+)\(\)\s*\.\s*(Show|ShowDialog)\(")]
    private static partial Regex OpensWindow();

    /// <summary>Imports windows from XAML files. The first MainWindow found becomes the first screen.</summary>
    /// <param name="projectName">The name for the project if the main window has no title.</param>
    public static ImportResult Import(IReadOnlyList<string> xamlPaths, string projectName)
    {
        if (xamlPaths.Count == 0)
        {
            throw new ImportException("Choose at least one XAML file.");
        }

        var windows = new List<WindowSource>();
        foreach (var path in xamlPaths)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ImportException($"Could not read \"{path}\": {ex.Message}", ex);
            }

            var events = Path.ChangeExtension(path, ".Events.g.cs");
            windows.Add(new WindowSource(path, text, File.Exists(events) ? File.ReadAllText(events) : null));
        }

        return Import(windows, projectName, ReadAsset);

        static byte[]? ReadAsset(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>One window to import: its XAML, and the builder's generated event code if there is one.</summary>
    public sealed record WindowSource(string Path, string Xaml, string? EventsCode);

    public static ImportResult Import(IReadOnlyList<WindowSource> windows, string projectName, Func<string, byte[]?> readAsset)
    {
        var warnings = new List<string>();
        var parsed = new List<(WindowSource Source, XElement Root, string ClassName)>();
        foreach (var window in windows)
        {
            XElement root;
            try
            {
                root = XDocument.Parse(window.Xaml).Root!;
            }
            catch (XmlException ex)
            {
                throw new ImportException($"\"{Path.GetFileName(window.Path)}\" is not valid XAML: {ex.Message}", ex);
            }

            if (root.Name.LocalName is not ("Window" or "Page" or "UserControl"))
            {
                throw new ImportException($"\"{Path.GetFileName(window.Path)}\" is a {root.Name.LocalName}, not a window. Choose window XAML files.");
            }

            var className = ((string?)root.Attribute(Xaml + "Class"))?.Split('.').Last() ?? Path.GetFileNameWithoutExtension(window.Path);
            parsed.Add((window, root, className));
        }

        // The main window opens first in the exported app, so it is the first screen.
        parsed = [.. parsed.OrderBy(p => p.ClassName == "MainWindow" ? 0 : 1)];

        var screenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var screens = new List<(ScreenDocument Screen, string ClassName, string? EventsCode)>();
        foreach (var (source, root, className) in parsed)
        {
            var name = UniqueName(ScreenName(className), screenNames);
            var context = new Context(Path.GetDirectoryName(source.Path) ?? "", name, readAsset, warnings);
            var screen = ReadScreen(root, context) with
            {
                Id = screens.Count == 0 ? ScreenDocument.DefaultId : Guid.NewGuid().ToString("N"),
                Name = name,
            };
            screens.Add((screen, className, source.EventsCode));
        }

        // Button actions from the builder's own event code: which window a button opens.
        var byClass = screens.ToDictionary(s => s.ClassName, s => s.Screen.Id, StringComparer.Ordinal);
        var linked = screens.Select(s => s.EventsCode is null ? s.Screen : s.Screen with
        {
            Controls = s.Screen.Controls.ConvertAll(c => WithActions(c, ReadActions(s.EventsCode), byClass)),
        }).ToImmutableList();

        var title = (string?)parsed[0].Root.Attribute("Title");
        var document = ProjectDocument.CreateBlank() with
        {
            Name = string.IsNullOrWhiteSpace(title) ? projectName : title,
            Screens = linked,
        };

        // Anything still invalid is reported rather than producing a project that will not open.
        var errors = DocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new ImportException("The imported design is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "• " + e)));
        }

        return new ImportResult(document, warnings);
    }

    /// <summary>"MainWindow" becomes the screen "Main", "SettingsWindow" becomes "Settings".</summary>
    private static string ScreenName(string className)
    {
        var name = className.EndsWith("Window", StringComparison.Ordinal) && className.Length > "Window".Length
            ? className[..^"Window".Length]
            : className;
        return Identifier().IsMatch(name) ? name : "Screen";
    }

    private sealed record Context(string Folder, string ScreenName, Func<string, byte[]?> ReadAsset, List<string> Warnings)
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Warn(string message) => Warnings.Add($"{ScreenName}: {message}");
    }

    private static ScreenDocument ReadScreen(XElement window, Context context)
    {
        var content = Children(window).FirstOrDefault();
        var (width, height) = ScreenSize(window, content, context);
        var screen = new ScreenDocument { Width = width, Height = height };
        if (content is null)
        {
            return screen;
        }

        var controls = new List<ControlDocument>();
        var isCanvas = content.Name.LocalName == "Canvas";
        var isPlainGrid = content.Name.LocalName == "Grid" && !HasTracks(content);
        if (isCanvas || isPlainGrid)
        {
            foreach (var child in Children(content))
            {
                if (ReadControl(child, context) is { } control)
                {
                    var (bounds, anchor) = isCanvas ? CanvasPlacement(child, control, screen, context) : GridPlacement(child, control, screen, context);
                    controls.Add(control.WithBounds(bounds) with { Anchor = anchor });
                }
            }
        }
        else if (ReadControl(content, context) is { } whole)
        {
            // A root StackPanel or Grid with rows becomes a container filling the screen.
            controls.Add(whole.WithBounds(new ControlBounds(0, 0, width, height)) with
            {
                Anchor = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom,
            });
        }

        return screen with { Controls = [.. controls] };
    }

    private static (int Width, int Height) ScreenSize(XElement window, XElement? content, Context context)
    {
        int? Size(XElement? element, string property) => element is null ? null : Number(element, property) is { } value and > 0 ? (int)Math.Round(value) : null;

        var width = Size(content, "Width") ?? Size(content, "MinWidth");
        var height = Size(content, "Height") ?? Size(content, "MinHeight");
        if (width is null || height is null)
        {
            var windowWidth = Size(window, "Width");
            var windowHeight = Size(window, "Height");
            if (windowWidth is not null || windowHeight is not null)
            {
                context.Warn("The screen size is taken from the window's size, which includes its frame and title bar.");
            }

            width ??= windowWidth ?? ScreenDocument.DefaultWidth;
            height ??= windowHeight ?? ScreenDocument.DefaultHeight;
        }

        return (Math.Clamp(width.Value, 100, 10000), Math.Clamp(height.Value, 100, 10000));
    }

    /// <summary>Where a control in the root Grid is: its alignment and margins become anchors.</summary>
    private static (ControlBounds Bounds, AnchorEdges Anchor) GridPlacement(XElement element, ControlDocument control, ScreenDocument screen, Context context)
    {
        var margin = Margin(element);
        var (x, width, horizontal) = Axis(Text(element, "HorizontalAlignment") ?? "Stretch", margin.Left, margin.Right, Number(element, "Width"), control.Width, screen.Width, AnchorEdges.Left, AnchorEdges.Right, control.Name, context);
        var (y, height, vertical) = Axis(Text(element, "VerticalAlignment") ?? "Stretch", margin.Top, margin.Bottom, Number(element, "Height"), control.Height, screen.Height, AnchorEdges.Top, AnchorEdges.Bottom, control.Name, context);
        return (Fit(new ControlBounds(x, y, width, height), control, screen, context), horizontal | vertical);
    }

    private static (int Start, int Size, AnchorEdges Anchor) Axis(
        string alignment, double before, double after, double? size, int defaultSize, int extent,
        AnchorEdges start, AnchorEdges end, string name, Context context)
    {
        switch (alignment)
        {
            case "Left" or "Top":
                return ((int)Math.Round(before), (int)Math.Round(size ?? defaultSize), start);
            case "Right" or "Bottom":
                var length = (int)Math.Round(size ?? defaultSize);
                return ((int)Math.Round(extent - after - length), length, end);
            case "Stretch" when size is null:
                return ((int)Math.Round(before), (int)Math.Round(extent - before - after), start | end);
            default:
                // Centred (explicitly, or stretched with a fixed size): kept where it is at the
                // design size, following the left or top edge.
                var fixedSize = (int)Math.Round(size ?? defaultSize);
                context.Warn($"\"{name}\" is centred; it now keeps its place from the {(start == AnchorEdges.Left ? "left" : "top")} edge instead.");
                return ((int)Math.Round(before + (extent - before - after - fixedSize) / 2), fixedSize, start);
        }
    }

    private static (ControlBounds Bounds, AnchorEdges Anchor) CanvasPlacement(XElement element, ControlDocument control, ScreenDocument screen, Context context)
    {
        var width = (int)Math.Round(Number(element, "Width") ?? control.Width);
        var height = (int)Math.Round(Number(element, "Height") ?? control.Height);
        var anchor = AnchorEdges.None;
        int x;
        if (Number(element, "Canvas.Right") is { } right && Number(element, "Canvas.Left") is null)
        {
            x = (int)Math.Round(screen.Width - right - width);
            anchor |= AnchorEdges.Right;
        }
        else
        {
            x = (int)Math.Round(Number(element, "Canvas.Left") ?? 0);
            anchor |= AnchorEdges.Left;
        }

        int y;
        if (Number(element, "Canvas.Bottom") is { } bottom && Number(element, "Canvas.Top") is null)
        {
            y = (int)Math.Round(screen.Height - bottom - height);
            anchor |= AnchorEdges.Bottom;
        }
        else
        {
            y = (int)Math.Round(Number(element, "Canvas.Top") ?? 0);
            anchor |= AnchorEdges.Top;
        }

        return (Fit(new ControlBounds(x, y, width, height), control, screen, context), anchor);
    }

    /// <summary>Keeps a control inside the screen and at least its type's minimum size.</summary>
    private static ControlBounds Fit(ControlBounds bounds, ControlDocument control, ScreenDocument screen, Context context)
    {
        var definition = ControlCatalog.Get(control.Type);
        var width = Math.Clamp(bounds.Width, definition.MinWidth, screen.Width);
        var height = Math.Clamp(bounds.Height, definition.MinHeight, screen.Height);
        var fitted = new ControlBounds(Math.Clamp(bounds.X, 0, screen.Width - width), Math.Clamp(bounds.Y, 0, screen.Height - height), width, height);
        if (fitted != bounds)
        {
            context.Warn($"\"{control.Name}\" was moved or resized to fit on the screen.");
        }

        return fitted;
    }

    /// <summary>
    /// A control and everything inside it, or null (with a warning) for something the builder
    /// cannot represent. Its position is set by the caller.
    /// </summary>
    private static ControlDocument? ReadControl(XElement element, Context context)
    {
        var kind = element.Name.LocalName;

        // The builder's own GroupBox: a Grid holding a GroupBox (the frame) and a StackPanel.
        if (kind == "Grid" && Children(element).ToList() is [{ Name.LocalName: "GroupBox" } frame, { Name.LocalName: "StackPanel" } inner] && !Children(frame).Any())
        {
            return GroupBox(frame, inner, context);
        }

        // The builder's own Image: a Grid with the designed box, holding the picture.
        if (kind == "Grid" && !HasTracks(element) && element.Attribute(Xaml + "Name") is null
            && Children(element).ToList() is [{ Name.LocalName: "Image" } picture])
        {
            return ReadControl(picture, context);
        }

        if (kind == "GroupBox")
        {
            return Children(element).FirstOrDefault() is { Name.LocalName: "StackPanel" } content
                ? GroupBox(element, content, context)
                : GroupBox(element, null, context);
        }

        ControlType? type = kind switch
        {
            "Label" or "TextBlock" => ControlType.Label,
            "Button" => ControlType.Button,
            "TextBox" => ControlType.TextBox,
            "PasswordBox" => ControlType.PasswordBox,
            "CheckBox" => ControlType.CheckBox,
            "RadioButton" => ControlType.RadioButton,
            "ComboBox" => ControlType.ComboBox,
            "ListBox" => ControlType.ListBox,
            "Slider" => ControlType.Slider,
            "ProgressBar" => ControlType.ProgressBar,
            "DatePicker" => ControlType.DatePicker,
            "Image" => ControlType.Image,
            "StackPanel" => ControlType.StackPanel,
            "Grid" => ControlType.Grid,
            _ => null,
        };
        if (type is not { } controlType)
        {
            var label = (string?)element.Attribute(Xaml + "Name") ?? (string?)element.Attribute("Name");
            context.Warn(label is null ? $"Left out a {kind}: the builder has no {kind}." : $"Left out {kind} \"{label}\": the builder has no {kind}.");
            return null;
        }

        var name = Name(element, controlType, context);

        var definition = ControlCatalog.Get(controlType);
        var properties = definition.CreateDefaultProperties(name);
        properties = controlType switch
        {
            ControlType.Label => properties with { Text = kind == "TextBlock" ? Text(element, "Text") ?? element.Value : Content(element) },
            ControlType.Button => properties with { Text = Content(element) },
            ControlType.TextBox => properties with
            {
                Text = Text(element, "Text") ?? "",
                IsMultiline = Text(element, "AcceptsReturn") == "True" ? true : null,
            },
            ControlType.CheckBox or ControlType.RadioButton => properties with
            {
                Text = Content(element),
                IsChecked = Text(element, "IsChecked") == "True",
            },
            ControlType.ComboBox or ControlType.ListBox => properties with { Items = Items(element) },
            ControlType.Slider or ControlType.ProgressBar => Range(element, properties, name, context),
            ControlType.Image => Picture(element, properties, name, context),
            ControlType.StackPanel => properties with
            {
                Orientation = Text(element, "Orientation") == "Horizontal" ? StackOrientation.Horizontal : StackOrientation.Vertical,
            },
            ControlType.Grid => properties with { Rows = 1, Columns = 1 },
            _ => properties,
        };

        var control = new ControlDocument
        {
            Id = Guid.NewGuid(),
            Type = controlType,
            Name = name,
            Properties = Style(element, properties, definition, name, context),
            Children = definition.IsContainer ? [] : null,
        }.WithBounds(new ControlBounds(0, 0, definition.DefaultWidth, definition.DefaultHeight));

        return controlType switch
        {
            ControlType.StackPanel => Stack(control, element, context),
            ControlType.Grid => Grid(control, element, context),
            _ => control,
        };
    }

    private static ControlDocument GroupBox(XElement frame, XElement? content, Context context)
    {
        var name = Name(frame, ControlType.GroupBox, context);
        var definition = ControlCatalog.Get(ControlType.GroupBox);
        var properties = definition.CreateDefaultProperties(name) with
        {
            Text = Text(frame, "Header") ?? "",
            Orientation = content is not null && Text(content, "Orientation") == "Horizontal" ? StackOrientation.Horizontal : StackOrientation.Vertical,
        };
        var group = new ControlDocument
        {
            Id = Guid.NewGuid(),
            Type = ControlType.GroupBox,
            Name = name,
            Properties = Style(frame, properties, definition, name, context),
            Children = [],
        }.WithBounds(new ControlBounds(0, 0, definition.DefaultWidth, definition.DefaultHeight));
        return content is null ? group : Stack(group, content, context);
    }

    /// <summary>
    /// A stack's children, each keeping its size along the stack. The gap before each child
    /// (its leading margin) becomes the stack's spacing.
    /// </summary>
    private static ControlDocument Stack(ControlDocument stack, XElement panel, Context context)
    {
        var vertical = stack.Properties.Orientation != StackOrientation.Horizontal;
        var children = new List<ControlDocument>();
        var gaps = new List<int>();
        foreach (var element in Children(panel))
        {
            if (ReadControl(element, context) is not { } child)
            {
                continue;
            }

            var margin = Margin(element);
            if (children.Count > 0)
            {
                gaps.Add((int)Math.Round(vertical ? margin.Top : margin.Left));
            }

            var size = vertical ? Number(element, "Height") : Number(element, "Width");
            children.Add(size is { } s
                ? vertical ? child with { Height = (int)Math.Round(s) } : child with { Width = (int)Math.Round(s) }
                : child);
        }

        var spacing = gaps.Count > 0 ? Math.Clamp(gaps[0], 0, ControlDefinition.MaxSpacing) : stack.Properties.Spacing ?? 0;
        if (gaps.Distinct().Count() > 1)
        {
            context.Warn($"\"{stack.Name}\" had different gaps between its controls; they are all {spacing} now.");
        }

        return stack with
        {
            Properties = stack.Properties with { Spacing = spacing },
            Children = [.. children.Select(c => FitMinimum(c))],
        };
    }

    private static ControlDocument Grid(ControlDocument grid, XElement panel, Context context)
    {
        var rows = Tracks(panel, "RowDefinition", "Height", grid.Name, context);
        var columns = Tracks(panel, "ColumnDefinition", "Width", grid.Name, context);
        var children = new List<ControlDocument>();
        foreach (var element in Children(panel))
        {
            if (ReadControl(element, context) is not { } child)
            {
                continue;
            }

            var row = Math.Clamp((int)(Number(element, "Grid.Row") ?? 0), 0, rows.Count - 1);
            var column = Math.Clamp((int)(Number(element, "Grid.Column") ?? 0), 0, columns.Count - 1);
            var rowSpan = Math.Clamp((int)(Number(element, "Grid.RowSpan") ?? 1), 1, rows.Count - row);
            var columnSpan = Math.Clamp((int)(Number(element, "Grid.ColumnSpan") ?? 1), 1, columns.Count - column);
            children.Add(FitMinimum(child) with
            {
                Row = row,
                Column = column,
                RowSpan = rowSpan > 1 ? rowSpan : null,
                ColumnSpan = columnSpan > 1 ? columnSpan : null,
            });
        }

        static ImmutableList<string>? Canonical(List<string> sizes) => sizes.All(s => s == "*") ? null : [.. sizes];
        return grid with
        {
            Properties = grid.Properties with
            {
                Rows = rows.Count,
                Columns = columns.Count,
                RowSizes = Canonical(rows),
                ColumnSizes = Canonical(columns),
            },
            Children = [.. children],
        };
    }

    /// <summary>A Grid's row or column sizes in the builder's notation; "Auto" becomes a share.</summary>
    private static List<string> Tracks(XElement panel, string definition, string property, string name, Context context)
    {
        var sizes = new List<string>();
        foreach (var track in panel.Descendants().Where(e => e.Name.LocalName == definition))
        {
            var text = Text(track, property) ?? "*";
            if (GridTrackSize.TryParse(text, out var size))
            {
                sizes.Add(size.ToString());
            }
            else
            {
                context.Warn($"\"{name}\" had a {(definition == "RowDefinition" ? "row" : "column")} sized \"{text}\"; it is an equal share now.");
                sizes.Add("*");
            }
        }

        return sizes.Count == 0 ? ["*"] : sizes[..Math.Min(sizes.Count, ControlDefinition.MaxRowsOrColumns)];
    }

    private static ControlDocument FitMinimum(ControlDocument control)
    {
        var definition = ControlCatalog.Get(control.Type);
        return control with { Width = Math.Max(control.Width, definition.MinWidth), Height = Math.Max(control.Height, definition.MinHeight) };
    }

    private static ControlProperties Range(XElement element, ControlProperties properties, string name, Context context)
    {
        var minimum = (int)Math.Round(Number(element, "Minimum") ?? 0);
        var maximum = (int)Math.Round(Number(element, "Maximum") ?? (element.Name.LocalName == "Slider" ? 10 : 100));
        var value = (int)Math.Round(Number(element, "Value") ?? minimum);
        if (DesignEditor.ValidateRange(minimum, maximum, value) is not null)
        {
            context.Warn($"\"{name}\" had a range the builder cannot use; it is 0 to 100 now.");
            return properties with { Minimum = 0, Maximum = 100, Value = Math.Clamp(value, 0, 100) };
        }

        return properties with { Minimum = minimum, Maximum = maximum, Value = value };
    }

    /// <summary>An Image's picture, read from the file its Source names, relative to the XAML file.</summary>
    private static ControlProperties Picture(XElement element, ControlProperties properties, string name, Context context)
    {
        properties = properties with { Stretch = Text(element, "Stretch") == "Fill" ? ImageStretch.Fill : ImageStretch.Uniform };
        if (Text(element, "Source") is not { Length: > 0 } source)
        {
            return properties;
        }

        var relative = source.Replace("pack://application:,,,", "", StringComparison.OrdinalIgnoreCase).TrimStart('/');
        var bytes = relative.Contains("://", StringComparison.Ordinal) ? null : context.ReadAsset(Path.Combine(context.Folder, relative));
        if (bytes is null || ImageFile.Validate(bytes) is not null)
        {
            context.Warn($"\"{name}\" shows \"{source}\", which could not be read as a picture; it is empty now.");
            return properties;
        }

        return properties with { ImageData = Convert.ToBase64String(bytes) };
    }

    /// <summary>Text size, bold and colours, where the builder can represent them.</summary>
    private static ControlProperties Style(XElement element, ControlProperties properties, ControlDefinition definition, string name, Context context)
    {
        if (definition.HasFont)
        {
            if (Number(element, "FontSize") is { } size)
            {
                properties = properties with { FontSize = Math.Clamp((int)Math.Round(size), ControlDefinition.MinFontSize, ControlDefinition.MaxFontSize) };
            }

            if (Text(element, "FontWeight") is "Bold" or "SemiBold" or "ExtraBold" or "Black" or "Heavy")
            {
                properties = properties with { IsBold = true };
            }

            properties = properties with { Foreground = Color(element, "Foreground", name, context) };
        }

        return definition.HasBackground ? properties with { Background = Color(element, "Background", name, context) } : properties;
    }

    private static string? Color(XElement element, string property, string name, Context context)
    {
        if (Text(element, property) is not { } text)
        {
            return null;
        }

        // "#RRGGBB", or "#AARRGGBB" when fully opaque.
        var hex = text.Length == 9 && text.StartsWith("#FF", StringComparison.OrdinalIgnoreCase) ? "#" + text[3..] : text;
        if (ControlColor.TryParse(hex, out var color) && hex.StartsWith('#'))
        {
            return color;
        }

        context.Warn($"\"{name}\" had the {property.ToLowerInvariant()} \"{text}\", which the builder cannot represent; it is left at the standard colour.");
        return null;
    }

    /// <summary>
    /// The text a content control shows: its Content attribute, or text or a TextBlock inside
    /// it. In WPF a single underscore marks an access key and two show one underscore.
    /// </summary>
    private static string Content(XElement element)
    {
        var content = Text(element, "Content");
        if (content is null && Children(element).FirstOrDefault(c => c.Name.LocalName == "TextBlock") is { } block)
        {
            content = Text(block, "Text") ?? block.Value;
        }

        content ??= element.Nodes().OfType<XText>().Any() ? element.Value.Trim() : "";
        return Regex.Replace(content, "__|_", m => m.Value == "__" ? "_" : "");
    }

    private static ImmutableList<string> Items(XElement element) =>
    [
        .. Children(element)
            .Select(item => item.Name.LocalName is "ComboBoxItem" or "ListBoxItem" ? Text(item, "Content") ?? item.Value : item.Value)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0),
    ];

    /// <summary>A control's name: its x:Name if it is a usable identifier, otherwise one made up.</summary>
    private static string Name(XElement element, ControlType type, Context context)
    {
        var name = (string?)element.Attribute(Xaml + "Name") ?? (string?)element.Attribute("Name");
        if (name is null || !Identifier().IsMatch(name))
        {
            for (var n = 1; ; n++)
            {
                var candidate = $"{type}{n}";
                if (!context.Names.Contains(candidate))
                {
                    name = candidate;
                    break;
                }
            }
        }
        else if (context.Names.Contains(name))
        {
            var unique = UniqueName(name, context.Names);
            context.Warn($"Two controls were named \"{name}\"; the second is \"{unique}\" now.");
            name = unique;
        }

        context.Names.Add(name);
        return name;
    }

    private static string UniqueName(string name, HashSet<string> taken)
    {
        var unique = name;
        for (var n = 2; taken.Contains(unique); n++)
        {
            unique = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + n.ToString(CultureInfo.InvariantCulture);
        }

        taken.Add(unique);
        return unique;
    }

    private static ControlDocument WithActions(ControlDocument control, Dictionary<string, string?> actions, Dictionary<string, string> screenByClass) => control with
    {
        Properties = control.Type == ControlType.Button && actions.TryGetValue(control.Name, out var target)
            ? target is null
                ? control.Properties with { ClosesScreen = true }
                : screenByClass.TryGetValue(target, out var screenId) ? control.Properties with { OpensScreen = screenId } : control.Properties
            : control.Properties,
        Children = control.Children?.ConvertAll(child => WithActions(child, actions, screenByClass)),
    };

    /// <summary>
    /// Button actions in generated event code: control name to the class of the window it opens,
    /// or to null for a button that closes its window.
    /// </summary>
    private static Dictionary<string, string?> ReadActions(string code)
    {
        var actions = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (Match handler in ClickHandler().Matches(code))
        {
            var body = handler.Groups["body"].Value;
            if (OpensWindow().Match(body) is { Success: true } opens)
            {
                actions[handler.Groups[1].Value] = opens.Groups[1].Success ? opens.Groups[1].Value : opens.Groups[3].Value;
            }
            else if (body.Contains("Close();", StringComparison.Ordinal))
            {
                actions[handler.Groups[1].Value] = null;
            }
        }

        return actions;
    }

    private static bool HasTracks(XElement grid) =>
        grid.Elements().Any(e => e.Name.LocalName is "Grid.RowDefinitions" or "Grid.ColumnDefinitions");

    /// <summary>An element's child elements, leaving out property elements such as Grid.RowDefinitions.</summary>
    private static IEnumerable<XElement> Children(XElement element) => element.Elements().Where(e => !e.Name.LocalName.Contains('.'));

    /// <summary>An attribute, or a property element (&lt;Button.Content&gt;) holding plain text.</summary>
    private static string? Text(XElement element, string property) =>
        (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == property && a.Name.Namespace != Xaml)
        ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == $"{element.Name.LocalName}.{property}" && !e.HasElements)?.Value;

    private static double? Number(XElement element, string property) =>
        Text(element, property) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;

    private static (double Left, double Top, double Right, double Bottom) Margin(XElement element)
    {
        var parts = (Text(element, "Margin") ?? "0").Split(',', ' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
        return parts.Length switch
        {
            1 => (parts[0], parts[0], parts[0], parts[0]),
            2 => (parts[0], parts[1], parts[0], parts[1]),
            4 => (parts[0], parts[1], parts[2], parts[3]),
            _ => (0, 0, 0, 0),
        };
    }
}
