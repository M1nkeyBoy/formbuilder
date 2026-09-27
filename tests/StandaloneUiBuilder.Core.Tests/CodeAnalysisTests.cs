using StandaloneUiBuilder.CodeAnalysis;

namespace StandaloneUiBuilder.Core.Tests;

/// <summary>IntelliSense for the code window, on the layout demo's Settings screen (Server, Level, a Save command…).</summary>
public class CodeAnalysisTests
{
    private static ScreenCodeAnalyzer Analyzer(ProjectPlatform platform = ProjectPlatform.Wpf)
    {
        var document = ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj")) with { Platform = platform };
        return new ScreenCodeAnalyzer(document, document.Screens[1]);
    }

    [Fact]
    public async Task GoodCodeHasNoProblems()
    {
        using var analyzer = Analyzer();
        Assert.Equal("SettingsViewModel", analyzer.ClassName);
        Assert.Empty(await analyzer.DiagnoseAsync("using System.Text;\n\npartial void OnSave() => Server = new StringBuilder(\"saved\").ToString();\n"));
        Assert.Empty(await analyzer.DiagnoseAsync(""));
    }

    [Fact]
    public async Task ProblemsArePlacedInTheCodeWindowsText()
    {
        using var analyzer = Analyzer();
        const string code = "using System.Text;\r\n\r\npartial void OnSave()\r\n{\r\n    Sever = \"saved\";\r\n}\r\n";
        var problem = Assert.Single(await analyzer.DiagnoseAsync(code));
        Assert.True(problem.IsError);
        Assert.Equal("CS0103", problem.Id);
        Assert.Equal((5, 5), (problem.Line, problem.Column));
        Assert.Equal("Sever", code.Substring(problem.Start, problem.Length));
        Assert.Contains("Sever", problem.Message);
    }

    [Fact]
    public async Task AMissingBraceIsShownAtTheEnd()
    {
        using var analyzer = Analyzer();
        const string code = "partial void OnSave()\n{\n    Server = \"x\";\n";
        var problem = Assert.Single(await analyzer.DiagnoseAsync(code), p => p.Id == "CS1513");
        Assert.True(problem.Start >= 0 && problem.Start <= code.Length);
    }

    [Fact]
    public async Task TheGeneratedPartUsesThePlatformsTypes()
    {
        // A number is whole in Windows Forms, and a double in WPF.
        const string code = "partial void OnLevelChanged() { int whole = Level; }\n";
        using var wpf = Analyzer(ProjectPlatform.Wpf);
        Assert.Contains(await wpf.DiagnoseAsync(code), p => p.Id == "CS0266");
        using var winForms = Analyzer(ProjectPlatform.WinForms);
        Assert.DoesNotContain(await winForms.DiagnoseAsync(code), p => p.IsError);
    }

    [Fact]
    public async Task CompletionOffersTheViewModelsMembersAndDotNets()
    {
        using var analyzer = Analyzer();
        var code = "partial void OnSave() => Ser";
        var list = await analyzer.CompleteAsync(code, code.Length);
        Assert.NotNull(list);
        Assert.Equal((code.Length - 3, 3), (list.Start, list.Length));
        var server = Assert.Single(list.Items, i => i.Text == "Server");
        Assert.Equal("Property", server.Kind);
        Assert.Contains(list.Items, i => i.Text == "Save");

        var edit = await analyzer.ChooseAsync(code, list, server);
        Assert.Equal((code.Length - 3, 3, "Server"), (edit!.Start, edit.Length, edit.NewText));
        Assert.Contains("string", await analyzer.DescribeAsync(list, server));

        // Members of the property's type after a dot.
        code = "partial void OnSave() => Server.";
        Assert.True(analyzer.ShouldComplete(code, code.Length, '.'));
        var members = await analyzer.CompleteAsync(code, code.Length, '.');
        Assert.Contains(members!.Items, i => i.Text == "Trim");
        Assert.Contains(members.Items, i => i.Text == "Length");
    }

    [Fact]
    public async Task CompletionWorksAfterUsingDirectives()
    {
        using var analyzer = Analyzer();
        const string code = "using System.Text;\n\nprivate StringB";
        var list = await analyzer.CompleteAsync(code, code.Length);
        Assert.Contains(list!.Items, i => i.Text == "StringBuilder");
        Assert.Equal(code.Length - "StringB".Length, list.Start);
    }

    [Fact]
    public async Task HoverDescribesWhatIsUnderIt()
    {
        using var analyzer = Analyzer();
        const string code = "partial void OnSave() => Server = \"x\";";
        var info = await analyzer.QuickInfoAsync(code, code.IndexOf("Server", StringComparison.Ordinal) + 2);
        Assert.Contains("string", info);
        Assert.Contains("Server", info);
        Assert.Null(await analyzer.QuickInfoAsync(code, code.Length));
    }

    [Fact]
    public async Task SignatureHelpListsOverloadsAndTheArgument()
    {
        using var analyzer = Analyzer();
        var code = "partial void OnSave() => Server = Math.Max(1, ";
        var help = await analyzer.SignaturesAsync(code, code.Length);
        Assert.NotNull(help);
        Assert.True(help.Signatures.Count > 3);
        Assert.Equal(1, help.ActiveParameter);
        Assert.All(help.Signatures, s => Assert.Equal(2, s.Parameters.Count));

        code = "partial void OnSave() => Server = new string(";
        help = await analyzer.SignaturesAsync(code, code.Length);
        Assert.Contains(help!.Signatures, s => s.Prefix == "String(");
        Assert.Null(await analyzer.SignaturesAsync("partial void OnSave() => Server = \"x\";", 10));
    }
}
