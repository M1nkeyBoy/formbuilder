using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using StandaloneUiBuilder.CodeAnalysis;

namespace StandaloneUiBuilder.CodeEditing;

/// <summary>A suggestion in the completion list: its name, what kind of thing it is, and, when chosen, its description.</summary>
internal sealed class CompletionEntry(ScreenCodeAnalyzer analyzer, string code, CodeCompletionList list, CodeCompletion item) : ICompletionData
{
    private TextBlock? description;

    public ImageSource? Image => null;

    public string Text => item.FilterText;

    public object Content => new TextBlock
    {
        Inlines =
        {
            new System.Windows.Documents.Run(Glyph(item.Kind) + " ") { Foreground = GlyphBrush(item.Kind) },
            new System.Windows.Documents.Run(item.Text),
        },
    };

    /// <summary>Filled in when shown, as describing every suggestion up front would be slow.</summary>
    public object Description
    {
        get
        {
            if (description is null)
            {
                description = new TextBlock { Text = item.Kind, MaxWidth = 420, TextWrapping = TextWrapping.Wrap };
                var shown = description;
                _ = Task.Run(() => analyzer.DescribeAsync(list, item)).ContinueWith(
                    t => shown.Text = t.Result ?? item.Kind,
                    CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
            }

            return description;
        }
    }

    public double Priority => 0;

    /// <summary>The suggestion's name, which screen readers say.</summary>
    public override string ToString() => item.Text;

    /// <summary>Replaces what has been typed with the suggestion as Roslyn inserts it (with type arguments, for example).</summary>
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var edit = Task.Run(() => analyzer.ChooseAsync(code, list, item)).GetAwaiter().GetResult();
        var text = edit?.NewText ?? item.Text;
        textArea.Document.Replace(completionSegment, text);
        if (edit?.CaretAfter is { } caret && edit.Start == completionSegment.Offset)
        {
            textArea.Caret.Offset = Math.Min(textArea.Document.TextLength, completionSegment.Offset + (caret - edit.Start));
        }
    }

    private static string Glyph(string kind) => kind switch
    {
        "Method" or "ExtensionMethod" => "ƒ",
        "Property" => "▪",
        "Field" or "Local" or "Parameter" or "Constant" or "RangeVariable" => "●",
        "Event" => "ϟ",
        "Class" or "Record" => "◆",
        "Structure" or "RecordStruct" => "◇",
        "Interface" => "○",
        "Enum" or "EnumMember" => "≡",
        "Namespace" => "{}",
        "Keyword" => "⌘",
        _ => "·",
    };

    private static Brush GlyphBrush(string kind) => kind switch
    {
        "Method" or "ExtensionMethod" => Brushes.MediumPurple,
        "Property" or "Field" or "Local" or "Parameter" or "Constant" => Brushes.SteelBlue,
        "Class" or "Record" or "Structure" or "RecordStruct" or "Interface" or "Enum" or "EnumMember" => Brushes.DarkGoldenrod,
        _ => Brushes.Gray,
    };
}

/// <summary>The overloads of a call, for the insight window: one at a time, with the current argument in bold.</summary>
internal sealed class Overloads(CodeSignatures signatures) : IOverloadProvider
{
    private int selectedIndex = signatures.Active;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = Math.Clamp(value, 0, Count - 1);
            PropertyChanged?.Invoke(this, new(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentIndexText)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentContent)));
        }
    }

    public int Count => signatures.Signatures.Count;

    public string? CurrentIndexText => Count > 1 ? $"{SelectedIndex + 1} of {Count}" : null;

    public object CurrentHeader
    {
        get
        {
            var signature = signatures.Signatures[SelectedIndex];
            var text = new TextBlock();
            text.Inlines.Add(signature.Prefix);
            for (var i = 0; i < signature.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    text.Inlines.Add(", ");
                }

                text.Inlines.Add(new System.Windows.Documents.Run(signature.Parameters[i])
                {
                    FontWeight = i == signatures.ActiveParameter ? FontWeights.Bold : FontWeights.Normal,
                });
            }

            text.Inlines.Add(signature.Suffix);
            return text;
        }
    }

    public object? CurrentContent => null;
}
