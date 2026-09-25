using System.Text.RegularExpressions;

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
}
