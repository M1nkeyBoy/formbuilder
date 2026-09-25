using System.Text.RegularExpressions;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Output;

/// <summary>Naming rules shared by the C# output generators.</summary>
public static partial class CodeNames
{
    public static IReadOnlySet<string> CSharpKeywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
        "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
    };

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    /// <summary>Turns a project name into a C# namespace: "Customer form" becomes "CustomerForm".</summary>
    public static string ToNamespace(string projectName)
    {
        var words = NonAlphanumeric().Split(projectName).Where(w => w.Length > 0);
        var name = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (name.Length == 0)
        {
            return "GeneratedApp";
        }

        return char.IsDigit(name[0]) ? "App" + name : name;
    }

    /// <summary>Each Image's picture, as a file for an exported project (see <see cref="ImageFile.ExportPath"/>).</summary>
    public static IEnumerable<GeneratedFile> ImageFiles(ProjectDocument document, string folder = "")
    {
        foreach (var screen in document.Screens)
        {
            foreach (var image in ControlTree.All(screen.Controls))
            {
                if (image.Properties.ImageData is { } data && ImageFile.TryDecode(data, out var bytes))
                {
                    yield return GeneratedFile.Binary(folder + ImageFile.ExportPath(screen, image), bytes);
                }
            }
        }
    }

    /// <summary>
    /// The class a screen becomes. The first screen is the application's main window, named
    /// "Main" plus the suffix (MainWindow, MainForm) so the startup code never has to change;
    /// every other screen is named after itself (SettingsWindow).
    /// </summary>
    public static string ScreenClassName(ProjectDocument document, ScreenDocument screen, string suffix) =>
        screen.Id == document.MainScreen.Id ? "Main" + suffix : screen.Name + suffix;

    /// <summary>
    /// Screens whose classes would clash: a later screen named "Main" would take the first
    /// screen's class name. Compared without case because the class names are also file names.
    /// </summary>
    public static IEnumerable<string> CheckScreenClassNames(ProjectDocument document, string suffix, string kind)
    {
        var taken = new Dictionary<string, ScreenDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in document.Screens)
        {
            var name = ScreenClassName(document, screen, suffix);
            if (taken.TryGetValue(name, out var other))
            {
                yield return $"Screen \"{screen.Name}\" would become {name}, the same {kind} as screen \"{other.Name}\". Rename it.";
            }
            else
            {
                taken[name] = screen;
            }
        }
    }
}
