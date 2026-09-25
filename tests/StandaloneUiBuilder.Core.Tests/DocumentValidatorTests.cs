using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class DocumentValidatorTests
{
    private static ControlDocument Button(string name, int x = 10, int y = 10, int width = 100, int height = 30) => new()
    {
        Id = Guid.NewGuid(),
        Type = ControlType.Button,
        Name = name,
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Properties = new ControlProperties { Text = name },
    };

    private static ProjectDocument WithControls(params ControlDocument[] controls)
    {
        var document = ProjectDocument.CreateBlank();
        return document.WithScreen(document.MainScreen with { Controls = [.. controls] });
    }

    [Fact]
    public void ControlInsideScreenIsValid()
    {
        Assert.Empty(DocumentValidator.Validate(WithControls(Button("Button1", 700, 570, 100, 30))));
    }

    [Theory]
    [InlineData(-1, 10, 100, 30)]
    [InlineData(10, -1, 100, 30)]
    [InlineData(701, 10, 100, 30)]
    [InlineData(10, 571, 100, 30)]
    [InlineData(10, 10, 29, 30)]
    [InlineData(10, 10, 100, 19)]
    public void OutOfBoundsOrUndersizedControlIsRejected(int x, int y, int width, int height)
    {
        var errors = DocumentValidator.Validate(WithControls(Button("Button1", x, y, width, height)));

        Assert.Single(errors);
    }

    [Fact]
    public void MinimumSizeIsNamedInTheMessage()
    {
        var screen = new ScreenDocument();

        var error = DocumentValidator.ValidateBounds(screen, ControlType.ComboBox, new ControlBounds(0, 0, 10, 30));

        Assert.Equal("Width must be at least 40 for a ComboBox.", error);
    }

    [Fact]
    public void DuplicateNamesAreRejectedIgnoringCase()
    {
        var errors = DocumentValidator.Validate(WithControls(Button("Submit"), Button("submit")));

        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1Button")]
    [InlineData("My Button")]
    [InlineData("Button-1")]
    public void InvalidNamesAreRejected(string name)
    {
        Assert.NotNull(DocumentValidator.ValidateName(new ScreenDocument(), Guid.NewGuid(), name));
    }

    [Theory]
    [InlineData("Button1")]
    [InlineData("_private")]
    [InlineData("SubmitButton")]
    public void ValidNamesAreAccepted(string name)
    {
        Assert.Null(DocumentValidator.ValidateName(new ScreenDocument(), Guid.NewGuid(), name));
    }

    [Fact]
    public void RenamingAControlToItsOwnNameIsAllowed()
    {
        var button = Button("Button1");
        var document = WithControls(button);

        Assert.Null(DocumentValidator.ValidateName(document.MainScreen, button.Id, "Button1"));
    }

    [Fact]
    public void DuplicateAndEmptyIdsAreRejected()
    {
        var first = Button("Button1");
        var duplicate = Button("Button2") with { Id = first.Id };
        var empty = Button("Button3") with { Id = Guid.Empty };

        var errors = DocumentValidator.Validate(WithControls(first, duplicate, empty));

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void UnknownControlTypeIsRejected()
    {
        var errors = DocumentValidator.Validate(WithControls(Button("Button1") with { Type = (ControlType)99 }));

        Assert.Single(errors);
    }

    [Fact]
    public void InvalidScreenSizeIsRejected()
    {
        var document = ProjectDocument.CreateBlank();
        document = document.WithScreen(document.MainScreen with { Width = 0, GridSize = 0 });

        Assert.Equal(2, DocumentValidator.Validate(document).Count);
    }
}
