using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Libraries;

/// <summary>
/// Finds the controls in a library's assemblies by reading their metadata; no code in them
/// runs. A control is a public class, with a public constructor that takes nothing, that
/// derives (through the library's own classes) from the platform's control classes. Its
/// settable text, on-or-off, number and enum properties are the ones the builder offers.
/// </summary>
public static class ControlScanner
{
    /// <summary>The most properties the builder lists for one control.</summary>
    public const int MaxProperties = 120;

    /// <param name="platform">The platform whose controls to find.</param>
    /// <param name="scan">The assemblies whose controls to list: the library's own.</param>
    /// <param name="references">
    /// Every assembly the library brings, including <paramref name="scan"/>, for following base
    /// classes and enums defined in the library's dependencies.
    /// </param>
    public static IReadOnlyList<LibraryControl> Scan(ProjectPlatform platform, IEnumerable<string> scan, IEnumerable<string> references)
    {
        using var index = new TypeIndex(references.Concat(scan).Distinct(StringComparer.OrdinalIgnoreCase));
        var scanned = new HashSet<string>(scan.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var controls = new List<LibraryControl>();
        foreach (var type in index.Types.Where(t => scanned.Contains(t.Assembly.Path)))
        {
            if (Describe(platform, index, type) is { } control)
            {
                controls.Add(control);
            }
        }

        return controls.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.TypeName, StringComparer.Ordinal).ToList();
    }

    private static LibraryControl? Describe(ProjectPlatform platform, TypeIndex index, IndexedType type)
    {
        var reader = type.Assembly.Reader;
        var definition = reader.GetTypeDefinition(type.Handle);
        var attributes = definition.Attributes;
        if ((attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public
            || (attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0
            || !HasPublicEmptyConstructor(reader, definition)
            || Hidden(reader, definition.GetCustomAttributes())
            || HelperByName(type.Name, type.Namespace))
        {
            return null;
        }

        var typeParameters = definition.GetGenericParameters().Select(p => reader.GetString(reader.GetGenericParameter(p).Name)).ToImmutableList();
        if (typeParameters.Count > 0 && platform != ProjectPlatform.Blazor)
        {
            return null;
        }

        // Follow the base classes through the library to the platform's own.
        var chain = new List<IndexedType> { type };
        string? frameworkBase = null;
        var current = type;
        for (var depth = 0; depth < 40; depth++)
        {
            var baseName = BaseTypeName(current.Assembly.Reader, current.Assembly.Reader.GetTypeDefinition(current.Handle));
            if (baseName is null)
            {
                return null;
            }

            if (index.Find(baseName) is { } next)
            {
                chain.Add(next);
                current = next;
                continue;
            }

            frameworkBase = baseName;
            break;
        }

        if (frameworkBase is null || !PlatformControls.IsControlBase(platform, frameworkBase))
        {
            return null;
        }

        var properties = new List<LibraryProperty>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (PlatformControls.ContentProperty(platform, frameworkBase) is { } content)
        {
            properties.Add(new LibraryProperty { Name = content, Type = "System.String" });
            names.Add(content);
        }

        foreach (var link in chain)
        {
            foreach (var property in Properties(platform, index, link))
            {
                if (properties.Count < MaxProperties && names.Add(property.Name))
                {
                    properties.Add(property);
                }
            }
        }

        var (width, height) = DefaultSize(type.Name);
        return new LibraryControl
        {
            TypeName = type.FullName,
            Assembly = type.Assembly.Name,
            Width = width,
            Height = height,
            TypeParameters = typeParameters.Count > 0 ? typeParameters : null,
            Properties = [.. properties],
        };
    }

    private static IEnumerable<LibraryProperty> Properties(ProjectPlatform platform, TypeIndex index, IndexedType type)
    {
        var reader = type.Assembly.Reader;
        var definition = reader.GetTypeDefinition(type.Handle);
        foreach (var handle in definition.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            var name = reader.GetString(property.Name);
            var setter = property.GetAccessors().Setter;
            if (setter.IsNil)
            {
                continue;
            }

            var setterAttributes = reader.GetMethodDefinition(setter).Attributes;
            if ((setterAttributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public || (setterAttributes & MethodAttributes.Static) != 0)
            {
                continue;
            }

            var custom = property.GetCustomAttributes();
            if (Hidden(reader, custom))
            {
                continue;
            }

            // A Blazor component's settable values are its parameters.
            if (platform == ProjectPlatform.Blazor && !Has(reader, custom, "Microsoft.AspNetCore.Components.ParameterAttribute"))
            {
                continue;
            }

            var signature = property.DecodeSignature(TypeNames.Instance, genericContext: null);
            var typeName = TypeNames.Unwrap(signature.ReturnType);
            if (LibraryValues.IsSimpleType(typeName) && typeName != "System.Type")
            {
                yield return new LibraryProperty { Name = name, Type = typeName };
            }
            else if (index.EnumMembers(typeName) is { Count: > 0 } members)
            {
                yield return new LibraryProperty { Name = name, Type = typeName, Choices = members };
            }
        }
    }

    private static string? BaseTypeName(MetadataReader reader, TypeDefinition definition)
    {
        // A generic base, such as ItemsControlBase<T>, stands for the generic class itself.
        var handle = definition.BaseType;
        return handle.IsNil ? null : TypeNames.NameOf(reader, handle);
    }

    private static bool HasPublicEmptyConstructor(MetadataReader reader, TypeDefinition definition)
    {
        foreach (var handle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) != ".ctor"
                || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (method.Attributes & MethodAttributes.Static) != 0)
            {
                continue;
            }

            var blob = reader.GetBlobReader(method.Signature);
            blob.ReadSignatureHeader();
            if (blob.ReadCompressedInteger() == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if attributes hide the type or property from designers: [Browsable(false)], [ToolboxItem(false)] and the like.</summary>
    private static bool Hidden(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            var name = AttributeName(reader, attribute);
            switch (name)
            {
                case "System.ObsoleteAttribute":
                case "System.Runtime.CompilerServices.CompilerGeneratedAttribute":
                    return true;
                case "System.ComponentModel.BrowsableAttribute":
                case "System.ComponentModel.ToolboxItemAttribute":
                case "System.ComponentModel.DesignTimeVisibleAttribute":
                    if (FirstArgument(reader, attribute) is byte flag && flag == 0)
                    {
                        return true;
                    }

                    break;
                case "System.ComponentModel.EditorBrowsableAttribute":
                case "System.ComponentModel.DesignerSerializationVisibilityAttribute":
                    // EditorBrowsableState.Never is 1; DesignerSerializationVisibility.Hidden is 0.
                    var state = FirstArgument(reader, attribute, isInt: true);
                    if ((name.EndsWith("EditorBrowsableAttribute", StringComparison.Ordinal) && state is 1)
                        || (name.EndsWith("DesignerSerializationVisibilityAttribute", StringComparison.Ordinal) && state is 0))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool Has(MetadataReader reader, CustomAttributeHandleCollection attributes, string fullName) =>
        attributes.Any(h => AttributeName(reader, reader.GetCustomAttribute(h)) == fullName);

    private static string? AttributeName(MetadataReader reader, CustomAttribute attribute) => attribute.Constructor.Kind switch
    {
        HandleKind.MemberReference => TypeNames.NameOf(reader, reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
        HandleKind.MethodDefinition => TypeNames.NameOf(reader, reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
        _ => null,
    };

    /// <summary>The attribute's first constructor argument, when it is a bool (as a byte) or an enum/int.</summary>
    private static object? FirstArgument(MetadataReader reader, CustomAttribute attribute, bool isInt = false)
    {
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.Length < 3 || blob.ReadUInt16() != 1)
        {
            return null;
        }

        try
        {
            return isInt ? (blob.RemainingBytes >= 4 ? blob.ReadInt32() : null) : blob.ReadByte();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static readonly string[] HelperSuffixes =
        ["Item", "Cell", "Presenter", "Adorner", "Thumb", "Chrome", "Part", "Host", "Renderer", "Decorator", "Row", "Column", "Panel",
         "Window", "Header", "Footer", "Base", "Template", "Converter", "Behavior", "Behaviour", "Handler", "Page", "Element", "Container",
         "ContentControl", "Events", "Items", "Settings", "Templates", "Field", "Fields", "Columns", "Model", "Directive", "Directives"];

    private static readonly string[] HelperWords =
        ["PropertyGridEditor", "RowControl", "CellControl", "CellsControl", "FilterControl", "Indicator", "DropArea", "DetailsView", "HeaderRow", "ContentViewer", "AreaControl"];

    private static readonly string[] HelperNamespaceParts = [".Themes", ".Primitives", ".Internal", ".Design", ".Automation", ".Helpers", ".Utils", ".Utilities", ".Converters"];

    /// <summary>Parts of controls rather than controls to place: grid cells, item containers, adorners and the like.</summary>
    private static bool HelperByName(string name, string ns) =>
        HelperSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal) && name.Length > s.Length)
        || HelperWords.Any(w => name.Contains(w, StringComparison.Ordinal))
        || HelperNamespaceParts.Any(p => ns.Contains(p, StringComparison.Ordinal))
        || name.StartsWith('_');

    private static readonly string[] LargeWords =
        ["Grid", "Chart", "Schedule", "Diagram", "Tree", "ListView", "List", "Calendar", "Map", "Gantt", "Kanban", "Pivot", "Spreadsheet",
         "RichText", "Viewer", "Carousel", "Gauge", "Editor", "Table", "Tab", "Dock", "Ribbon", "Report", "Timeline", "Accordion", "Layout"];

    private static (int Width, int Height) DefaultSize(string name) =>
        LargeWords.Any(w => name.Contains(w, StringComparison.Ordinal)) ? (320, 200) : (160, 32);
}

/// <summary>Which of a platform's own classes library controls derive from.</summary>
public static class PlatformControls
{
    private static readonly HashSet<string> WpfDenied =
    [
        "Window", "NavigationWindow", "Page", "DataTemplateSelector", "StyleSelector", "ValidationRule", "GridViewColumn", "DataGridColumn",
        "DataGridBoundColumn", "DataGridTextColumn", "DataGridTemplateColumn", "DataGridCheckBoxColumn", "DataGridComboBoxColumn",
        "DataGridHyperlinkColumn", "RowDefinition", "ColumnDefinition", "ItemsPanelTemplate", "ControlTemplate", "DataTemplate",
        "HierarchicalDataTemplate", "ItemContainerTemplate", "ToolTip", "ContextMenu", "Popup", "MenuItem", "Separator", "ResizeGrip",
        "ContentPresenter", "ItemsPresenter", "ScrollContentPresenter", "AdornedElementPlaceholder", "ToolBarTray",
    ];

    private static readonly HashSet<string> WinFormsDenied =
    [
        "Form", "CommonDialog", "FileDialog", "ColorDialog", "FontDialog", "OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog",
        "PrintDialog", "PageSetupDialog", "Timer", "ToolTip", "ErrorProvider", "HelpProvider", "ImageList", "BindingSource", "NotifyIcon",
        "ContextMenuStrip", "Menu", "MainMenu", "ContextMenu", "MenuItem", "ColumnHeader", "ListViewItem", "ListViewGroup", "TreeNode",
        "AxHost", "ApplicationContext", "NativeWindow", "ToolStripItem", "ToolStripMenuItem", "ToolStripButton", "ToolStripLabel",
        "ToolStripDropDown", "ToolStripDropDownMenu", "ToolStripControlHost", "ToolStripSeparator", "ToolStripDropDownItem",
        "DataGridViewColumn", "DataGridViewCell", "DataGridViewRow", "DataGridViewBand", "MdiClient",
    ];

    private static readonly HashSet<string> WinUIDenied =
    [
        "Page", "Window", "DataTemplateSelector", "StyleSelector", "ItemsPanelTemplate", "ControlTemplate", "DataTemplate", "Flyout",
        "MenuFlyout", "FlyoutBase", "ToolTip", "RowDefinition", "ColumnDefinition", "SwipeItem", "CommandBarFlyout", "MenuFlyoutItem",
        "MenuFlyoutItemBase", "Popup", "ContentPresenter", "ItemsPresenter", "ScrollContentPresenter", "IconSource",
    ];

    private static readonly HashSet<string> MauiAllowed =
    [
        "View", "ContentView", "TemplatedView", "Layout", "Grid", "StackLayout", "VerticalStackLayout", "HorizontalStackLayout",
        "AbsoluteLayout", "FlexLayout", "ScrollView", "Border", "Frame", "Button", "Label", "Entry", "Editor", "Image", "ImageButton",
        "CheckBox", "Switch", "Slider", "Stepper", "Picker", "DatePicker", "TimePicker", "ProgressBar", "ActivityIndicator",
        "CollectionView", "ListView", "CarouselView", "IndicatorView", "SearchBar", "RadioButton", "WebView", "GraphicsView", "BoxView",
        "RefreshView", "SwipeView", "ItemsView", "StructuredItemsView", "SelectableItemsView", "GroupableItemsView", "ReorderableItemsView",
        "InputView", "Shape", "Path", "Ellipse", "Rectangle", "Line", "Polygon", "Polyline", "RoundRectangle", "VisualElement",
    ];

    /// <summary>True if a class of the platform's, by full name, makes a library class that derives from it a control.</summary>
    public static bool IsControlBase(ProjectPlatform platform, string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        var ns = dot < 0 ? "" : fullName[..dot];
        var name = fullName[(dot + 1)..];
        var plain = name.Split('`')[0];
        return platform switch
        {
            ProjectPlatform.Wpf => (ns is "System.Windows.Controls" or "System.Windows.Controls.Primitives" or "System.Windows.Shapes" && !WpfDenied.Contains(plain))
                || (ns == "System.Windows" && plain is "FrameworkElement" or "UIElement"),
            ProjectPlatform.WinForms => ns == "System.Windows.Forms" && !WinFormsDenied.Contains(plain)
                && !plain.EndsWith("Column", StringComparison.Ordinal) && !plain.EndsWith("Cell", StringComparison.Ordinal)
                && !plain.EndsWith("Dialog", StringComparison.Ordinal) && !plain.EndsWith("EventArgs", StringComparison.Ordinal)
                && !(plain.StartsWith("ToolStrip", StringComparison.Ordinal) && plain is not ("ToolStrip" or "ToolStripContainer" or "ToolStripPanel")),
            ProjectPlatform.WinUI => (ns is "Microsoft.UI.Xaml.Controls" or "Microsoft.UI.Xaml.Controls.Primitives" or "Microsoft.UI.Xaml.Shapes"
                    && !WinUIDenied.Contains(plain) && !plain.EndsWith("Flyout", StringComparison.Ordinal) && !plain.EndsWith("Source", StringComparison.Ordinal))
                || (ns == "Microsoft.UI.Xaml" && plain is "FrameworkElement" or "UIElement"),
            ProjectPlatform.Maui => ns is "Microsoft.Maui.Controls" or "Microsoft.Maui.Controls.Shapes" && MauiAllowed.Contains(plain),
            ProjectPlatform.Blazor => (ns == "Microsoft.AspNetCore.Components" && plain is "ComponentBase" or "OwningComponentBase")
                || (ns == "Microsoft.AspNetCore.Components.Forms" && plain.StartsWith("Input", StringComparison.Ordinal)),
            _ => false,
        };
    }

    /// <summary>The text property a control gets from its platform class, which the builder offers first.</summary>
    public static string? ContentProperty(ProjectPlatform platform, string fullName)
    {
        var name = fullName[(fullName.LastIndexOf('.') + 1)..];
        return platform switch
        {
            ProjectPlatform.Wpf or ProjectPlatform.WinUI when name is "ContentControl" or "Button" or "ButtonBase" or "ToggleButton" or "CheckBox"
                or "RadioButton" or "Label" or "RepeatButton" or "HeaderedContentControl" or "Expander" or "GroupBox" => "Content",
            ProjectPlatform.WinForms => "Text",
            ProjectPlatform.Maui when name is "Button" or "Label" or "Entry" or "Editor" or "SearchBar" => "Text",
            _ => null,
        };
    }
}
