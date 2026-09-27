using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using StandaloneUiBuilder.CodeAnalysis;
using StandaloneUiBuilder.CodeEditing;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder;

/// <summary>
/// Edits a screen's view model code: C# members that every export writes into the view model,
/// such as the On… methods its commands and bindings call. The code is compiled as you type,
/// with the rest of the view model, as the export compiles it: problems are underlined and
/// listed, and completion, hover information and overload hints are the compiler's.
/// </summary>
public partial class CodeWindow : Window
{
    private readonly ScreenDocument screen;
    private readonly Squiggles squiggles = new();
    private readonly DispatcherTimer checkTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Task<ScreenCodeAnalyzer> analyzer;
    private IReadOnlyList<CodeProblem> problems = [];
    private CancellationTokenSource? checking;
    private CompletionWindow? completion;
    private OverloadInsightWindow? overloads;
    private ToolTip? hover;

    private sealed record HookItem(ViewModelHook Hook, string Title, string Description);

    private sealed record ProblemItem(CodeProblem Problem, string Text, string Glyph, Brush Brush);

    private CodeWindow(ProjectDocument document, ScreenDocument screen)
    {
        InitializeComponent();
        this.screen = screen;
        var className = Output.ViewModelCode.ClassName(document, screen);
        Title = $"Code — {className}";
        IntroText.Text = $"Members of {className}, the view model of screen \"{screen.Name}\". Every export writes them into the class, "
            + "so they can use its properties and implement its hooks. Using directives may come first. Ctrl+Space suggests, and the compiler checks the code as you type.";

        CodeBox.Text = screen.Code?.Replace("\n", Environment.NewLine, StringComparison.Ordinal) ?? "";
        CodeBox.SyntaxHighlighting = CodeColors.CSharp(EditorTheme.IsDark);
        CodeBox.Options.ConvertTabsToSpaces = true;
        CodeBox.Options.IndentationSize = 4;
        CodeBox.Options.EnableHyperlinks = false;
        CodeBox.TextArea.TextView.BackgroundRenderers.Add(squiggles);
        CodeBox.TextArea.SelectionBrush = (Brush)FindResource("AccentSoftBrush");
        CodeBox.TextArea.SelectionForeground = null;
        CodeBox.TextArea.Caret.CaretBrush = (Brush)FindResource("TextBrush");

        var properties = DataBindings.Properties(screen);
        var commands = DataBindings.Commands(screen);
        MembersText.Text = properties.Count + commands.Count == 0
            ? "The screen has no bindings or commands yet; bind controls in the inspector to give the view model properties."
            : "Properties: " + (properties.Count == 0 ? "none" : string.Join(", ", properties.Select(p => $"{p.Name} ({Describe(p.Kind)})")))
                + ". Commands: " + (commands.Count == 0 ? "none" : string.Join(", ", commands.Select(c => $"{c.Name}()"))) + ".";
        RefreshHooks();

        // The compiler is loaded in the background: the window opens at once, and checking and
        // suggestions start when it is ready (the first time takes a second or two).
        ProblemsSummary.Text = "Loading C#…";
        analyzer = Task.Run(() => new ScreenCodeAnalyzer(document, screen));
        checkTimer.Tick += (_, _) =>
        {
            checkTimer.Stop();
            _ = CheckAsync();
        };
        CodeBox.TextChanged += (_, _) =>
        {
            RefreshHooks();
            checkTimer.Stop();
            checkTimer.Start();
        };
        CodeBox.TextArea.TextEntered += TextArea_TextEntered;
        CodeBox.TextArea.TextEntering += TextArea_TextEntering;
        CodeBox.MouseHover += CodeBox_MouseHover;
        CodeBox.MouseHoverStopped += (_, _) => CloseHover();
        Loaded += (_, _) =>
        {
            CodeBox.Focus();
            _ = CheckAsync();
        };
        Closed += (_, _) =>
        {
            checking?.Cancel();
            analyzer.ContinueWith(t => t.Result.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
        };
    }

    /// <summary>Shows the window; returns the edited code, or null if the user cancelled.</summary>
    public static string? Edit(Window owner, ProjectDocument document, ScreenDocument screen)
    {
        var window = new CodeWindow(document, screen) { Owner = owner };
        return window.ShowDialog() == true ? window.CodeBox.Text : null;
    }

    private static string Describe(BindingKind kind) => kind switch
    {
        BindingKind.Text => "text",
        BindingKind.Flag => "on or off",
        BindingKind.Number => "number",
        BindingKind.Choice => "chosen item",
        _ => "date",
    };

    /// <summary>Compiles the code as it is now and shows what the compiler says.</summary>
    private async Task CheckAsync()
    {
        checking?.Cancel();
        checking = new CancellationTokenSource();
        var token = checking.Token;
        var code = CodeBox.Text;
        try
        {
            var compiler = await analyzer;
            var found = await Task.Run(() => compiler.DiagnoseAsync(code, token), token);
            if (token.IsCancellationRequested || code != CodeBox.Text)
            {
                return;
            }

            ShowProblems(found);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The editor still works without the compiler's help.
            ProblemsSummary.Text = $"C# checking is unavailable: {ex.Message}";
            App.Log($"Code checking failed: {ex}");
        }
    }

    private void ShowProblems(IReadOnlyList<CodeProblem> found)
    {
        problems = found;
        squiggles.Show(found);
        CodeBox.TextArea.TextView.InvalidateLayer(squiggles.Layer);
        var errors = found.Count(p => p.IsError);
        var warnings = found.Count - errors;
        ProblemsSummary.Text = found.Count == 0
            ? "No problems"
            : string.Join(", ", new[] { Plural(errors, "error"), Plural(warnings, "warning") }.Where(s => s.Length > 0));
        var error = (Brush)FindResource("ErrorBrush");
        ProblemsList.ItemsSource = found.Select(p => new ProblemItem(
            p,
            (p.Line > 0 ? $"Line {p.Line}: " : "In the generated part: ") + $"{p.Message} ({p.Id})",
            p.IsError ? "✕" : "⚠",
            p.IsError ? error : Brushes.DarkGoldenrod)).ToList();
    }

    private static string Plural(int count, string word) => count switch
    {
        0 => "",
        1 => $"1 {word}",
        _ => $"{count} {word}s",
    };

    /// <summary>Goes to a problem in the code.</summary>
    private void GoToProblem()
    {
        if (ProblemsList.SelectedItem is ProblemItem { Problem: { Start: >= 0 } problem })
        {
            var start = Math.Min(problem.Start, CodeBox.Document.TextLength);
            CodeBox.Select(start, Math.Min(problem.Length, CodeBox.Document.TextLength - start));
            CodeBox.ScrollTo(problem.Line, problem.Column);
            CodeBox.Focus();
        }
    }

    private void ProblemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => GoToProblem();

    private void ProblemsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GoToProblem();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Suggestions as you type (after a dot, or at the start of a name), and the overloads of a
    /// call after "(" or ",".
    /// </summary>
    private async void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1 || !analyzer.IsCompletedSuccessfully)
        {
            return;
        }

        var typed = e.Text[0];
        if (typed is '(' or ',')
        {
            await ShowOverloadsAsync();
        }
        else if (typed == ')')
        {
            overloads?.Close();
        }

        if (completion is null && (typed == '.' || char.IsLetter(typed) || typed == '_'))
        {
            var code = CodeBox.Text;
            var caret = CodeBox.CaretOffset;
            if (analyzer.Result.ShouldComplete(code, caret, typed))
            {
                await ShowCompletionAsync(typed);
            }
        }
    }

    /// <summary>
    /// A character that cannot be part of a name picks the highlighted suggestion first, as in
    /// Visual Studio, when the suggestion starts with what has been typed; otherwise the list
    /// closes, so a name of your own is never replaced.
    /// </summary>
    private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 0 || completion is null || char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_')
        {
            return;
        }

        var typed = CodeBox.Document.GetText(completion.StartOffset, Math.Max(0, CodeBox.CaretOffset - completion.StartOffset));
        if (e.Text[0] is '.' or '(' or ';' or ',' or ')' or '[' or ' ' && typed.Length > 0
            && completion.CompletionList.SelectedItem is { } chosen && chosen.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            completion.CompletionList.RequestInsertion(e);
        }
        else
        {
            completion.Close();
        }
    }

    private async Task ShowCompletionAsync(char? typed)
    {
        var compiler = await analyzer;
        var code = CodeBox.Text;
        var caret = CodeBox.CaretOffset;
        var list = await Task.Run(() => compiler.CompleteAsync(code, caret, typed));
        if (list is null || completion is not null)
        {
            return;
        }

        // Typing goes on while the suggestions are worked out: they still apply while the caret
        // is in the same name, after the same text, and narrow to what has been typed since.
        var now = CodeBox.CaretOffset;
        var document = CodeBox.Document;
        if (now < list.Start || now > document.TextLength || list.Start > code.Length
            || document.GetText(0, list.Start) != code[..list.Start]
            || !document.GetText(list.Start, now - list.Start).All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return;
        }

        completion = new CompletionWindow(CodeBox.TextArea)
        {
            StartOffset = list.Start,
            EndOffset = now,
            CloseWhenCaretAtBeginning = typed is null,
            // Wide enough for long names such as OnChosenServerChanged.
            Width = 300,
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
        };
        foreach (var item in list.Items)
        {
            completion.CompletionList.CompletionData.Add(new CompletionEntry(compiler, code, list, item));
        }

        // What is already typed of the name narrows the list.
        completion.CompletionList.SelectItem(document.GetText(list.Start, now - list.Start));
        completion.Closed += (_, _) => completion = null;
        completion.Show();
    }

    private async Task ShowOverloadsAsync()
    {
        var compiler = await analyzer;
        var code = CodeBox.Text;
        var caret = CodeBox.CaretOffset;
        var signatures = await Task.Run(() => compiler.SignaturesAsync(code, caret));
        overloads?.Close();
        if (signatures is null || code != CodeBox.Text)
        {
            return;
        }

        overloads = new OverloadInsightWindow(CodeBox.TextArea)
        {
            Provider = new Overloads(signatures),
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
        };
        overloads.Closed += (_, _) => overloads = null;
        overloads.Show();
    }

    /// <summary>Hovering shows the problem there, or what the compiler knows about the name.</summary>
    private async void CodeBox_MouseHover(object sender, MouseEventArgs e)
    {
        if (CodeBox.GetPositionFromPoint(e.GetPosition(CodeBox)) is not { } position)
        {
            return;
        }

        var offset = CodeBox.Document.GetOffset(position.Location);
        var here = problems.Where(p => p.Start >= 0 && offset >= p.Start && offset <= p.Start + Math.Max(p.Length, 1)).ToList();
        string? text = here.Count > 0 ? string.Join(Environment.NewLine, here.Select(p => $"{p.Message} ({p.Id})")) : null;
        if (text is null && analyzer.IsCompletedSuccessfully)
        {
            var code = CodeBox.Text;
            text = await Task.Run(() => analyzer.Result.QuickInfoAsync(code, offset));
        }

        if (text is null || !CodeBox.IsMouseOver)
        {
            return;
        }

        CloseHover();
        hover = new ToolTip
        {
            Content = new TextBlock { Text = text, MaxWidth = 520, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") },
            PlacementTarget = CodeBox,
            IsOpen = true,
        };
        e.Handled = true;
    }

    private void CloseHover()
    {
        if (hover is not null)
        {
            hover.IsOpen = false;
            hover = null;
        }
    }

    private void RefreshHooks()
    {
        var selected = (HooksList.SelectedItem as HookItem)?.Hook.Name;
        HooksList.ItemsSource = DataBindings.Hooks(screen)
            .Select(h => new HookItem(h, (DataBindings.Implements(CodeBox.Text, h) ? "✓ " : "") + h.Name + "()", h.Description))
            .ToList();
        HooksList.SelectedItem = HooksList.Items.Cast<HookItem>().FirstOrDefault(i => i.Hook.Name == selected);
    }

    private void AddHook()
    {
        if (HooksList.SelectedItem is not HookItem item)
        {
            return;
        }

        if (!DataBindings.Implements(CodeBox.Text, item.Hook))
        {
            var text = CodeBox.Text.TrimEnd();
            var stub = item.Hook.Stub.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
            CodeBox.Text = text.Length == 0 ? stub : text + Environment.NewLine + Environment.NewLine + stub;

            // The caret goes inside the new method's braces.
            CodeBox.CaretOffset = CodeBox.Text.TrimEnd().Length - 1;
        }

        RefreshHooks();
        CodeBox.Focus();
    }

    private void HooksList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddHook();

    private void AddHook_Click(object sender, RoutedEventArgs e) => AddHook();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Ctrl+Space suggests; Ctrl+Shift+Space shows the overloads of the call; Ctrl+Enter applies.</summary>
    private async void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DialogResult = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && (completion is not null || overloads is not null))
        {
            // Esc closes a suggestion list or overload hint, not the window.
            completion?.Close();
            overloads?.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (analyzer.IsCompletedSuccessfully)
            {
                completion?.Close();
                await ShowCompletionAsync(null);
            }
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            if (analyzer.IsCompletedSuccessfully)
            {
                await ShowOverloadsAsync();
            }
        }
    }
}
