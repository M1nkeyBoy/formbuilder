using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class ProjectDocumentTests
{
    [Fact]
    public void BlankDocumentHasDefaultScreen()
    {
        var document = ProjectDocument.CreateBlank();

        Assert.Equal(ProjectDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.NotEqual(Guid.Empty, document.ProjectId);
        Assert.Equal("Untitled", document.Name);
        Assert.Equal(800, document.MainScreen.Width);
        Assert.Equal(600, document.MainScreen.Height);
        Assert.Equal(10, document.MainScreen.GridSize);
        Assert.Empty(document.MainScreen.Controls);
    }

    [Fact]
    public void BlankDocumentsGetDistinctProjectIds()
    {
        Assert.NotEqual(ProjectDocument.CreateBlank().ProjectId, ProjectDocument.CreateBlank().ProjectId);
    }

    [Fact]
    public void BlankDocumentIsValid()
    {
        Assert.Empty(DocumentValidator.Validate(ProjectDocument.CreateBlank()));
    }

    [Theory]
    [InlineData(ControlType.Label)]
    [InlineData(ControlType.Button)]
    [InlineData(ControlType.TextBox)]
    [InlineData(ControlType.CheckBox)]
    [InlineData(ControlType.ComboBox)]
    public void EveryControlTypeIsRegistered(ControlType type)
    {
        var definition = ControlCatalog.Get(type);

        Assert.Equal(type, definition.Type);
        Assert.True(definition.DefaultWidth >= definition.MinWidth);
        Assert.True(definition.DefaultHeight >= definition.MinHeight);
    }

    [Fact]
    public void NormalizeKeepsOnlySupportedProperties()
    {
        var mixed = new ControlProperties { Text = "Hi", IsChecked = true, Items = ["A"] };

        var button = ControlCatalog.Get(ControlType.Button).Normalize(mixed);
        var combo = ControlCatalog.Get(ControlType.ComboBox).Normalize(new ControlProperties());

        Assert.Equal("Hi", button.Text);
        Assert.Null(button.IsChecked);
        Assert.Null(button.Items);
        Assert.Null(combo.Text);
        Assert.NotNull(combo.Items);
        Assert.Empty(combo.Items);
    }
}
