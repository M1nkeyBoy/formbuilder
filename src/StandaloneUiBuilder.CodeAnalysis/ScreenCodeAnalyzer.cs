using System.Collections.Immutable;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.Maui;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.WinUI;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.CodeAnalysis;

/// <summary>A compile error or warning in the code window's text.</summary>
/// <param name="Start">Offset in the code window's text; -1 for a problem outside it (in the generated part of the class).</param>
/// <param name="Line">1-based line in the code window; 0 when outside it.</param>
public sealed record CodeProblem(int Start, int Length, int Line, int Column, bool IsError, string Id, string Message);

/// <summary>A suggestion of the completion list.</summary>
public sealed record CodeCompletion(string Text, string FilterText, string Kind, CompletionItem Item);

/// <summary>Completion at a position: the suggestions, and the span of the code window's text they replace.</summary>
public sealed record CodeCompletionList(int Start, int Length, IReadOnlyList<CodeCompletion> Items, Document Document);

/// <summary>A change to the code window's text.</summary>
public sealed record CodeEdit(int Start, int Length, string NewText, int? CaretAfter);

/// <summary>The overloads of the method or constructor being called, and where the caret is in its arguments.</summary>
public sealed record CodeSignatures(IReadOnlyList<CodeSignature> Signatures, int Active, int ActiveParameter);

public sealed record CodeSignature(string Prefix, IReadOnlyList<string> Parameters, string Suffix);

/// <summary>
/// C# IntelliSense for a screen's code: the code window's text is compiled with Roslyn as part
/// of the screen's view model, together with the generated part of the class (its bound
/// properties, commands and hooks, with the types of the project's platform) and .NET's own
/// libraries, as the exported project compiles it. Positions in the code window are mapped to
/// the compiled file and back. Every method is safe to call from any thread.
/// </summary>
public sealed class ScreenCodeAnalyzer : IDisposable
{
    /// <summary>The implicit usings of the exported projects (Microsoft.NET.Sdk).</summary>
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static readonly ImmutableArray<MetadataReference> References = [.. Net100.References.All];

    private readonly AdhocWorkspace workspace;
    private readonly DocumentId codeDocument;
    private readonly string ns;

    public ScreenCodeAnalyzer(ProjectDocument document, ScreenDocument screen)
    {
        ns = CodeNames.ToNamespace(document.Name);
        ClassName = ViewModelCode.ClassName(document, screen);
        workspace = new AdhocWorkspace(MefHostServices.DefaultHost);
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "Screen",
            "Screen",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: References));
        workspace.AddDocument(project.Id, "GlobalUsings.g.cs", SourceText.From(GlobalUsings));
        workspace.AddDocument(project.Id, ViewModelCode.FileName(document, screen), SourceText.From(GeneratedPart(document, screen, ns)));
        codeDocument = workspace.AddDocument(project.Id, ViewModelCode.CodeFileName(document, screen), SourceText.From(Wrap("").Text)).Id;
    }

    /// <summary>The view model class the code belongs to.</summary>
    public string ClassName { get; }

    /// <summary>The generated part of the view model, with the types of the project's platform (WPF's for any platform).</summary>
    private static string GeneratedPart(ProjectDocument document, ScreenDocument screen, string ns) => document.Platform switch
    {
        ProjectPlatform.WinForms => WinFormsGenerator.ViewModel(document, screen, ns),
        ProjectPlatform.WinUI => WinUIGenerator.ViewModel(document, screen, ns),
        ProjectPlatform.Maui => MauiGenerator.ViewModel(document, screen, ns),
        ProjectPlatform.Blazor => BlazorGenerator.ViewModel(document, screen, ns),
        _ => WpfGenerator.ViewModelCode(document, screen, ns),
    };

    /// <summary>
    /// The compiled file for the code window's text, as the export writes it: using directives at
    /// the start above the namespace, the rest inside the class. Lines are kept as they are, so
    /// a line and column in the code window map to one in the file.
    /// </summary>
    private Wrapped Wrap(string code)
    {
        var source = SourceText.From(code);
        var lines = source.Lines.Select(l => source.ToString(l.Span)).ToList();
        var usings = ViewModelCode.LeadingUsingLines(lines);
        var file = new System.Text.StringBuilder();
        file.Append("#nullable enable\n");
        for (var i = 0; i < usings; i++)
        {
            file.Append(lines[i]).Append('\n');
        }

        file.Append($"namespace {ns};\n");
        file.Append($"public partial class {ClassName}\n{{\n");
        for (var i = usings; i < lines.Count; i++)
        {
            file.Append(lines[i]).Append('\n');
        }

        file.Append("}\n");
        return new Wrapped(source, SourceText.From(file.ToString()), usings);
    }

    /// <summary>The code window's text and its compiled file, with the mapping between their lines.</summary>
    private sealed record Wrapped(SourceText Code, SourceText File, int UsingLines)
    {
        private const int Header = 1;
        private const int ClassLines = 3;

        public string Text => File.ToString();

        public int ToFileLine(int line) => line < UsingLines ? Header + line : Header + line + ClassLines;

        public int? ToCodeLine(int fileLine) =>
            fileLine >= Header && fileLine < Header + UsingLines ? fileLine - Header
            : fileLine >= Header + UsingLines + ClassLines && fileLine < Header + ClassLines + Code.Lines.Count ? fileLine - Header - ClassLines
            : null;

        public int ToFile(int offset)
        {
            var position = Code.Lines.GetLinePosition(Math.Clamp(offset, 0, Code.Length));
            return File.Lines[ToFileLine(position.Line)].Start + position.Character;
        }

        public int? ToCode(int fileOffset)
        {
            var position = File.Lines.GetLinePosition(Math.Clamp(fileOffset, 0, File.Length));
            if (ToCodeLine(position.Line) is not { } line)
            {
                return null;
            }

            var codeLine = Code.Lines[line];
            return codeLine.Start + Math.Min(position.Character, codeLine.Span.Length);
        }
    }

    private (Document Document, Wrapped Wrapped) Snapshot(string code)
    {
        var wrapped = Wrap(code);
        var solution = workspace.CurrentSolution.WithDocumentText(codeDocument, wrapped.File);
        return (solution.GetDocument(codeDocument)!, wrapped);
    }

    /// <summary>The compile errors and warnings, in the order of the code.</summary>
    public async Task<IReadOnlyList<CodeProblem>> DiagnoseAsync(string code, CancellationToken cancellationToken = default)
    {
        var (document, wrapped) = Snapshot(code);
        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        var problems = new List<CodeProblem>();
        foreach (var diagnostic in compilation!.GetDiagnostics(cancellationToken))
        {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning) || diagnostic.IsSuppressed)
            {
                continue;
            }

            var isError = diagnostic.Severity == DiagnosticSeverity.Error;
            var location = diagnostic.Location;
            if (location.SourceTree == tree)
            {
                // A problem in the lines the export adds (a missing closing brace, say) is shown at the end of the code.
                var start = wrapped.ToCode(location.SourceSpan.Start) ?? wrapped.Code.Length;
                var end = Math.Max(start, wrapped.ToCode(location.SourceSpan.End) ?? start);
                var position = wrapped.Code.Lines.GetLinePosition(start);
                problems.Add(new CodeProblem(start, end - start, position.Line + 1, position.Character + 1, isError, diagnostic.Id, diagnostic.GetMessage()));
            }
            else if (isError)
            {
                problems.Add(new CodeProblem(-1, 0, 0, 0, isError, diagnostic.Id, diagnostic.GetMessage()));
            }
        }

        return [.. problems.Distinct().OrderBy(p => p.Start < 0 ? int.MaxValue : p.Start).ThenBy(p => p.IsError ? 0 : 1)];
    }

    /// <summary>True if typing the character just before the position should open the completion list.</summary>
    public bool ShouldComplete(string code, int position, char typed)
    {
        var (document, wrapped) = Snapshot(code);
        return CompletionService.GetService(document) is { } service
            && service.ShouldTriggerCompletion(wrapped.File, wrapped.ToFile(position), CompletionTrigger.CreateInsertionTrigger(typed));
    }

    /// <summary>The completion list at a position; null if there is nothing to suggest.</summary>
    public async Task<CodeCompletionList?> CompleteAsync(string code, int position, char? typed = null, CancellationToken cancellationToken = default)
    {
        var (document, wrapped) = Snapshot(code);
        if (CompletionService.GetService(document) is not { } service)
        {
            return null;
        }

        var trigger = typed is { } c ? CompletionTrigger.CreateInsertionTrigger(c) : CompletionTrigger.Invoke;
        var list = await service.GetCompletionsAsync(document, wrapped.ToFile(position), trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (list.ItemsList.Count == 0 || wrapped.ToCode(list.Span.Start) is not { } start || wrapped.ToCode(list.Span.End) is not { } end)
        {
            return null;
        }

        var items = list.ItemsList
            .Select(i => new CodeCompletion(i.DisplayTextPrefix + i.DisplayText + i.DisplayTextSuffix, i.FilterText, KindOf(i.Tags), i))
            .ToList();
        return new CodeCompletionList(start, end - start, items, document);
    }

    /// <summary>The change choosing a suggestion makes to the code window's text.</summary>
    public async Task<CodeEdit?> ChooseAsync(string code, CodeCompletionList list, CodeCompletion item, CancellationToken cancellationToken = default)
    {
        var wrapped = Wrap(code);
        if (CompletionService.GetService(list.Document) is not { } service)
        {
            return null;
        }

        var change = await service.GetChangeAsync(list.Document, item.Item, commitCharacter: null, cancellationToken).ConfigureAwait(false);
        var span = change.TextChange.Span;
        if (wrapped.ToCode(span.Start) is not { } start || wrapped.ToCode(span.End) is not { } end)
        {
            return null;
        }

        var caret = change.NewPosition is { } newPosition ? wrapped.ToCode(newPosition) : null;
        return new CodeEdit(start, end - start, change.TextChange.NewText ?? "", caret);
    }

    /// <summary>What a suggestion is: its signature and documentation, as text.</summary>
    public async Task<string?> DescribeAsync(CodeCompletionList list, CodeCompletion item, CancellationToken cancellationToken = default)
    {
        var service = CompletionService.GetService(list.Document);
        var description = service is null ? null : await service.GetDescriptionAsync(list.Document, item.Item, cancellationToken).ConfigureAwait(false);
        return description?.Text is { Length: > 0 } text ? text : null;
    }

    /// <summary>What is under the position: a symbol's signature and documentation, as text; null if nothing.</summary>
    public async Task<string?> QuickInfoAsync(string code, int position, CancellationToken cancellationToken = default)
    {
        var (document, wrapped) = Snapshot(code);
        if (QuickInfoService.GetService(document) is not { } service)
        {
            return null;
        }

        var info = await service.GetQuickInfoAsync(document, wrapped.ToFile(position), cancellationToken).ConfigureAwait(false);
        var text = info is null ? "" : string.Join(Environment.NewLine, info.Sections.Select(s => s.Text).Where(t => t.Length > 0));
        return text.Length > 0 ? text : null;
    }

    /// <summary>The overloads of the call the position is in the arguments of; null if it is not in a call.</summary>
    public async Task<CodeSignatures?> SignaturesAsync(string code, int position, CancellationToken cancellationToken = default)
    {
        var (document, wrapped) = Snapshot(code);
        var filePosition = wrapped.ToFile(position);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return null;
        }

        var token = root.FindToken(Math.Max(0, filePosition - 1));
        var arguments = token.Parent?.AncestorsAndSelf().OfType<ArgumentListSyntax>()
            .FirstOrDefault(a => a.OpenParenToken.Span.End <= filePosition && (a.CloseParenToken.IsMissing || filePosition <= a.CloseParenToken.SpanStart));
        if (arguments is null)
        {
            return null;
        }

        List<IMethodSymbol> methods;
        ISymbol? chosen;
        switch (arguments.Parent)
        {
            case InvocationExpressionSyntax invocation:
                methods = [.. model.GetMemberGroup(invocation.Expression, cancellationToken).OfType<IMethodSymbol>()];
                chosen = model.GetSymbolInfo(invocation, cancellationToken).Symbol;
                break;
            case BaseObjectCreationExpressionSyntax creation when model.GetTypeInfo(creation, cancellationToken).Type is INamedTypeSymbol type:
                methods = [.. type.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)];
                chosen = model.GetSymbolInfo(creation, cancellationToken).Symbol;
                break;
            default:
                return null;
        }

        if (methods.Count == 0)
        {
            return null;
        }

        var parameter = arguments.Arguments.GetSeparators().Count(s => s.SpanStart < filePosition);
        var index = chosen is IMethodSymbol method
            ? methods.FindIndex(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, method.OriginalDefinition))
            : -1;
        var active = index >= 0
            ? index
            : Math.Max(0, methods.FindIndex(m => m.Parameters.Length > parameter || (m.Parameters.Length > 0 && m.Parameters[^1].IsParams)));
        var format = SymbolDisplayFormat.MinimallyQualifiedFormat;
        var signatures = methods.Select(m => new CodeSignature(
            (m.MethodKind == MethodKind.Constructor ? m.ContainingType.Name : $"{m.ReturnType.ToDisplayString(format)} {m.ContainingType.Name}.{m.Name}") + "(",
            [.. m.Parameters.Select(p => p.ToDisplayString(format))],
            ")")).ToList();
        return new CodeSignatures(signatures, active, parameter);
    }

    private static string KindOf(ImmutableArray<string> tags) =>
        tags.FirstOrDefault(t => t is "Method" or "ExtensionMethod" or "Property" or "Field" or "Event" or "Local" or "Parameter" or "Class"
            or "Structure" or "Interface" or "Enum" or "EnumMember" or "Delegate" or "Namespace" or "Keyword" or "Snippet" or "Constant"
            or "TypeParameter" or "RangeVariable" or "Record" or "RecordStruct")
        ?? "";

    public void Dispose() => workspace.Dispose();
}
