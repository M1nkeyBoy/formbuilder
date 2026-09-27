using StandaloneUiBuilder.CodeAnalysis;

namespace StandaloneUiBuilder.Core.Tests;

public class CodeAnalysisTypingTests
{
    [Fact]
    public async Task TypingANameOffersSuggestions()
    {
        var document = ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "layout-demo.uibproj"));
        using var analyzer = new ScreenCodeAnalyzer(document, document.Screens[1]);
        var code = "partial void OnLevelChanged()\r\n{\r\nS}";
        var caret = code.Length - 1;
        Assert.True(analyzer.ShouldComplete(code, caret, 'S'), "typing a letter should suggest");
        var list = await analyzer.CompleteAsync(code, caret, 'S');
        Assert.NotNull(list);
        Assert.Contains(list.Items, i => i.Text == "Server");
    }
}
