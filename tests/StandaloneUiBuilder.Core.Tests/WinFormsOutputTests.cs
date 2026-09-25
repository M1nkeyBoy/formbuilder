using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.WinForms;

namespace StandaloneUiBuilder.Core.Tests;

public sealed class WinFormsOutputTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("uib-winforms-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static ProjectDocument Sample() =>
        ProjectFile.Load(Path.Combine(AppContext.BaseDirectory, "samples", "customer-form.uibproj"));

    private static string Designer(ProjectDocument document) => WinFormsGenerator.DesignerCode(document, "Test");

    private static ProjectDocument Single(ControlType type, string name, ControlProperties? properties = null, AnchorEdges anchor = AnchorEdges.Default)
    {
        var editor = new DesignEditor();
        var control = editor.AddControl(type, 20, 30);
        editor.Rename(control.Id, name);
        editor.SetAnchor(control.Id, anchor);
        var document = editor.Document;
        var only = document.MainScreen.Controls[0];
        if (properties is not null)
        {
            only = only with { Properties = ControlCatalog.Get(type).Normalize(properties) };
        }

        return document.WithScreen(document.MainScreen with { Controls = [only] });
    }

    private static void AssertValidCSharp(string code)
    {
        var errors = CSharpSyntaxTree.ParseText(code).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void EveryGeneratedFileIsValidCSharp()
    {
        foreach (var file in WinFormsGenerator.Generate(Sample(), "CustomerForm").Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            AssertValidCSharp(file.Content);
        }
    }

    [Fact]
    public void EachControlIsCreatedPlacedAndNamed()
    {
        var code = Designer(Sample());

        Assert.Contains("this.SubmitButton = new System.Windows.Forms.Button();", code);
        Assert.Contains("this.SubmitButton.Location = new System.Drawing.Point(150, 250);", code);
        Assert.Contains("this.SubmitButton.Size = new System.Drawing.Size(120, 32);", code);
        Assert.Contains("this.SubmitButton.Name = \"SubmitButton\";", code);
        Assert.Contains("this.SubmitButton.Text = \"Submit\";", code);
        Assert.Contains("this.NewsletterCheckBox.Checked = true;", code);
        Assert.Contains("this.PlanComboBox.Items.AddRange(new object[] { \"Basic\", \"Standard\", \"Premium\" });", code);
        Assert.Contains("this.PlanComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;", code);
        Assert.Contains("this.ClientSize = new System.Drawing.Size(800, 600);", code);
        Assert.Contains("this.Text = \"Customer form\";", code);
        Assert.Contains("private System.Windows.Forms.TextBox NameTextBox;", code);
    }

    [Fact]
    public void TabOrderFollowsTheDesignAndLaterControlsAreOnTop()
    {
        var code = Designer(Sample());
        var names = Sample().MainScreen.Controls.Select(c => c.Name).ToList();

        Assert.Contains($"this.{names[0]}.TabIndex = 0;", code);
        Assert.Contains($"this.{names[^1]}.TabIndex = {names.Count - 1};", code);

        // WinForms draws the first control added on top, so the last-drawn control is added first.
        var addOrder = names.Select(n => code.IndexOf($"this.Controls.Add(this.{n});", StringComparison.Ordinal)).ToList();
        Assert.All(addOrder, index => Assert.True(index > 0));
        Assert.Equal(addOrder.OrderByDescending(i => i), addOrder);
    }

    [Theory]
    [InlineData(AnchorEdges.Left | AnchorEdges.Top, "(System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)")]
    [InlineData(AnchorEdges.Right | AnchorEdges.Bottom, "(System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)")]
    [InlineData(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top, "(System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)")]
    public void AnchorsMapToAnchorStyles(AnchorEdges anchor, string expected)
    {
        var code = Designer(Single(ControlType.Button, "Go", anchor: anchor));

        Assert.Contains($"this.Go.Anchor = {expected};", code);
    }

    [Fact]
    public void FormIsResizableOnlyWhenAControlFollowsTheRightOrBottomEdge()
    {
        var fixedForm = Designer(Single(ControlType.Button, "Go"));
        var resizable = Designer(Single(ControlType.Button, "Go", anchor: AnchorEdges.Right | AnchorEdges.Top));

        Assert.Contains("this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;", fixedForm);
        Assert.Contains("this.MaximizeBox = false;", fixedForm);
        Assert.DoesNotContain("MinimumSize", fixedForm);
        Assert.Contains("this.MinimumSize = this.SizeFromClientSize(this.ClientSize);", resizable);
        Assert.DoesNotContain("FormBorderStyle", resizable);
    }

    [Fact]
    public void TextIsEscapedAndAmpersandsShowLiterally()
    {
        var code = Designer(Single(ControlType.Button, "Go", new ControlProperties { Text = "Save & \"close\"\\\n" }));

        Assert.Contains("this.Go.Text = \"Save && \\\"close\\\"\\\\\\n\";", code);
        AssertValidCSharp(code);
    }

    [Fact]
    public void TextBoxAmpersandsAreNotDoubled()
    {
        var code = Designer(Single(ControlType.TextBox, "Box", new ControlProperties { Text = "a & b" }));

        Assert.Contains("this.Box.Text = \"a & b\";", code);
    }

    [Theory]
    [InlineData("tab\there", "\"tab\\there\"")]
    [InlineData("bell\u0007", "\"bell\\u0007\"")]
    [InlineData("line\u2028sep", "\"line\\u2028sep\"")]
    [InlineData("😀", "\"\\uD83D\\uDE00\"")]
    public void LiteralsEscapeAwkwardCharacters(string value, string expected)
    {
        Assert.Equal(expected, WinFormsGenerator.Literal(value));
        AssertValidCSharp($"class C {{ string s = {WinFormsGenerator.Literal(value)}; }}");
    }

    [Fact]
    public void EventsAreWiredToHooks()
    {
        var designer = Designer(Sample());
        var events = WinFormsGenerator.EventsCode(Sample(), "CustomerForm");

        Assert.Contains("this.SubmitButton.Click += this.SubmitButton_Click;", designer);
        Assert.Contains("this.PlanComboBox.SelectedIndexChanged += this.PlanComboBox_SelectedIndexChanged;", designer);
        Assert.Contains("private void SubmitButton_Click(object? sender, System.EventArgs e) => OnSubmitButtonClick(e);", events);
        Assert.Contains("partial void OnNameTextBoxTextChanged(System.EventArgs e);", events);
        Assert.Contains("partial void OnNewsletterCheckBoxClick(System.EventArgs e);", events);
        Assert.DoesNotContain("TitleLabel", events);
    }

    [Fact]
    public void ControlNamedAfterACommonFormMemberIsDeclaredNew()
    {
        Assert.Contains("private new System.Windows.Forms.Button CancelButton;", Designer(Sample()));
        Assert.Contains("private System.Windows.Forms.Button SubmitButton;", Designer(Sample()));
    }

    [Theory]
    [InlineData("class")]
    [InlineData("MainForm")]
    [InlineData("Text")]
    [InlineData("Controls")]
    [InlineData("components")]
    public void NamesThatWouldBreakTheGeneratedCodeAreReported(string name)
    {
        var document = Single(ControlType.Button, name);

        Assert.Single(WinFormsGenerator.Check(document));
        Assert.Throws<ExportException>(() => WinFormsExporter.Export(document, directory));
    }

    [Fact]
    public void EmptyScreenGivesAnEmptyFormThatIsValidCSharp()
    {
        var code = Designer(ProjectDocument.CreateBlank());

        Assert.DoesNotContain("Controls.Add", code);
        AssertValidCSharp(code);
    }

    [Fact]
    public void ExportWritesTheProjectAndKeepsDeveloperFilesOnReExport()
    {
        var first = WinFormsExporter.Export(Sample(), directory);
        var formFile = Path.Combine(first.ProjectFolder, "MainForm.cs");
        File.AppendAllText(formFile, "// mine\n");

        var second = WinFormsExporter.Export(Sample(), directory);

        Assert.Equal(
            ["CustomerForm.csproj", "MainForm.cs", "MainForm.Designer.cs", "MainForm.Events.g.cs", "Program.cs"],
            Directory.GetFiles(first.ProjectFolder).Select(Path.GetFileName).Order());
        Assert.Equal(5, first.Created.Count);
        Assert.Empty(second.Created);
        Assert.Empty(second.Updated);
        Assert.EndsWith("// mine\n", File.ReadAllText(formFile));
        Assert.Contains("<UseWindowsForms>true</UseWindowsForms>", File.ReadAllText(Path.Combine(first.ProjectFolder, "CustomerForm.csproj")));
    }

    [Fact]
    public void AHandWrittenDesignerFileIsNeverReplaced()
    {
        var folder = Path.Combine(directory, "CustomerForm");
        Directory.CreateDirectory(folder);
        var designer = Path.Combine(folder, "MainForm.Designer.cs");
        File.WriteAllText(designer, "// hand written");

        Assert.Throws<ExportException>(() => WinFormsExporter.Export(Sample(), directory));
        Assert.Equal("// hand written", File.ReadAllText(designer));
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        Assert.Equal(Designer(Sample()), Designer(Sample()));
    }
}
