using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace StandaloneUiBuilder.CodeEditing;

/// <summary>C# colouring for the code window, in colours that read well on the editor's light or dark background.</summary>
internal static class CodeColors
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Comment"] = ("#008000", "#6A9955"),
        ["String"] = ("#A31515", "#CE9178"),
        ["Char"] = ("#A31515", "#CE9178"),
        ["Preprocessor"] = ("#808080", "#9B9B9B"),
        ["Punctuation"] = ("#1F1F1F", "#D4D4D4"),
        ["ValueTypeKeywords"] = ("#0000FF", "#569CD6"),
        ["ReferenceTypeKeywords"] = ("#0000FF", "#569CD6"),
        ["MethodCall"] = ("#74531F", "#DCDCAA"),
        ["NumberLiteral"] = ("#098658", "#B5CEA8"),
        ["ThisOrBaseReference"] = ("#0000FF", "#569CD6"),
        ["NullOrValueKeywords"] = ("#0000FF", "#569CD6"),
        ["Keywords"] = ("#0000FF", "#569CD6"),
        ["GotoKeywords"] = ("#8F08C4", "#C586C0"),
        ["ContextKeywords"] = ("#0000FF", "#569CD6"),
        ["ExceptionKeywords"] = ("#8F08C4", "#C586C0"),
        ["CheckedKeyword"] = ("#0000FF", "#569CD6"),
        ["UnsafeKeywords"] = ("#0000FF", "#569CD6"),
        ["OperatorKeywords"] = ("#0000FF", "#569CD6"),
        ["ParameterModifiers"] = ("#0000FF", "#569CD6"),
        ["Modifiers"] = ("#0000FF", "#569CD6"),
        ["Visibility"] = ("#0000FF", "#569CD6"),
        ["NamespaceKeywords"] = ("#0000FF", "#569CD6"),
        ["GetSetAddRemove"] = ("#0000FF", "#569CD6"),
        ["TrueFalse"] = ("#0000FF", "#569CD6"),
        ["TypeKeywords"] = ("#0000FF", "#569CD6"),
        ["SemanticKeywords"] = ("#0000FF", "#569CD6"),
    };

    /// <summary>The C# colouring, set to the editor's light or dark colours.</summary>
    public static IHighlightingDefinition CSharp(bool dark)
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        foreach (var color in definition.NamedHighlightingColors)
        {
            if (Palette.TryGetValue(color.Name, out var pair))
            {
                color.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(dark ? pair.Dark : pair.Light));
            }
            else if (dark && color.Foreground is not null)
            {
                // Anything else, in a colour that shows on a dark background.
                color.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString("#9CDCFE"));
            }
        }

        return definition;
    }
}
