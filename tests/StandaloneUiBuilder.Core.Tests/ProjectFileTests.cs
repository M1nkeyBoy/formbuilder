using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public sealed class ProjectFileTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("uib-tests-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static ProjectDocument PopulatedDocument()
    {
        var editor = new DesignEditor();
        var label = editor.AddControl(ControlType.Label, 20, 20);
        var button = editor.AddControl(ControlType.Button, 20, 60);
        editor.AddControl(ControlType.TextBox, 140, 20);
        var check = editor.AddControl(ControlType.CheckBox, 20, 100);
        var combo = editor.AddControl(ControlType.ComboBox, 140, 100);
        editor.Rename(button.Id, "SubmitButton");
        editor.SetText(label.Id, "Name:");
        editor.SetIsChecked(check.Id, true);
        editor.SetItems(combo.Id, ["Small", "Medium", "Large"]);
        editor.SetBounds(button.Id, new ControlBounds(23, 61, 117, 33));
        return editor.Document;
    }

    private static void AssertEquivalent(ProjectDocument expected, ProjectDocument actual)
    {
        Assert.Equal(expected.ProjectId, actual.ProjectId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Screen with { Controls = [] }, actual.Screen with { Controls = [] });
        Assert.Equal(expected.Screen.Controls.Count, actual.Screen.Controls.Count);
        for (var i = 0; i < expected.Screen.Controls.Count; i++)
        {
            var e = expected.Screen.Controls[i];
            var a = actual.Screen.Controls[i];
            Assert.Equal(e with { Properties = new() }, a with { Properties = new() });
            Assert.Equal(e.Properties.Text, a.Properties.Text);
            Assert.Equal(e.Properties.IsChecked, a.Properties.IsChecked);
            Assert.Equal(e.Properties.Items, a.Properties.Items);
        }
    }

    [Fact]
    public void EmptyProjectRoundTrips()
    {
        var document = ProjectDocument.CreateBlank();
        var path = Path.Combine(directory, "empty.uibproj");

        ProjectFile.Save(document, path);

        AssertEquivalent(document, ProjectFile.Load(path));
    }

    [Fact]
    public void PopulatedProjectRoundTripsIncludingOrderAndProperties()
    {
        var document = PopulatedDocument();
        var path = Path.Combine(directory, "populated.uibproj");

        ProjectFile.Save(document, path);
        var reopened = ProjectFile.Load(path);

        AssertEquivalent(document, reopened);
    }

    [Fact]
    public void SavingAgainProducesIdenticalFile()
    {
        var path = Path.Combine(directory, "repeat.uibproj");
        ProjectFile.Save(PopulatedDocument(), path);
        var first = File.ReadAllText(path);

        ProjectFile.Save(ProjectFile.Load(path), path);

        Assert.Equal(first, File.ReadAllText(path));
    }

    [Fact]
    public void SaveLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(directory, "clean.uibproj");

        ProjectFile.Save(PopulatedDocument(), path);
        ProjectFile.Save(PopulatedDocument(), path);

        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public void SerializedFormatMatchesDocumentedShape()
    {
        var json = ProjectFile.Serialize(PopulatedDocument());

        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.Contains("\"type\": \"Button\"", json);
        Assert.Contains("\"name\": \"SubmitButton\"", json);
        Assert.Contains("\"gridSize\": 10", json);
        Assert.DoesNotContain("bounds", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SampleProjectInDocsLoads()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "customer-form.uibproj");

        var document = ProjectFile.Load(sample);

        Assert.Equal(5, document.Screen.Controls.Select(c => c.Type).Distinct().Count());
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "no schema version")]
    [InlineData("""{ "name": "x" }""", "no schema version")]
    [InlineData("""{ "schemaVersion": 4, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screen": {} }""", "newer version")]
    [InlineData("""{ "schemaVersion": 0, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screen": {} }""", "not valid")]
    [InlineData("""{ "schemaVersion": 1, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971" }""", "not a valid project")]
    public void InvalidFilesAreRejectedWithAClearReason(string json, string expected)
    {
        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void UnsupportedControlTypeIsNamed()
    {
        var json = ProjectFile.Serialize(PopulatedDocument()).Replace("\"type\": \"Button\"", "\"type\": \"Slider\"");

        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains("\"Slider\"", ex.Message);
        Assert.Contains("SubmitButton", ex.Message);
    }

    [Fact]
    public void OutOfBoundsControlIsRejected()
    {
        var json = ProjectFile.Serialize(PopulatedDocument()).Replace("\"x\": 23", "\"x\": 790");

        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains("SubmitButton", ex.Message);
    }

    [Fact]
    public void MissingTypeSpecificPropertiesGetDefaults()
    {
        var json = """
            {
              "schemaVersion": 1,
              "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
              "name": "Minimal",
              "screen": {
                "controls": [
                  { "id": "9e651cb8-84b8-4140-a171-62075666768e", "type": "CheckBox", "name": "Agree",
                    "x": 10, "y": 10, "width": 100, "height": 20 }
                ]
              }
            }
            """;

        var control = Assert.Single(ProjectFile.Deserialize(json).Screen.Controls);

        Assert.Equal("", control.Properties.Text);
        Assert.False(control.Properties.IsChecked);
    }

    [Fact]
    public void LoadingAMissingFileReportsAnError()
    {
        Assert.Throws<ProjectFileException>(() => ProjectFile.Load(Path.Combine(directory, "missing.uibproj")));
    }

    [Fact]
    public void FailedSaveKeepsTheExistingFile()
    {
        var path = Path.Combine(directory, "keep.uibproj");
        ProjectFile.Save(PopulatedDocument(), path);
        var before = File.ReadAllText(path);

        // Saving into a directory that does not exist fails before anything is replaced.
        Assert.Throws<ProjectFileException>(() => ProjectFile.Save(ProjectDocument.CreateBlank(), Path.Combine(directory, "missing", "x.uibproj")));

        Assert.Equal(before, File.ReadAllText(path));
    }
}
