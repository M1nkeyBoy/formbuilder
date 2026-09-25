using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class AnchorTests
{
    private static readonly ScreenDocument Screen = new();

    private static ControlDocument Button(AnchorEdges anchor) => new()
    {
        Id = Guid.NewGuid(),
        Type = ControlType.Button,
        Name = "Button1",
        X = 100,
        Y = 50,
        Width = 120,
        Height = 30,
        Anchor = anchor,
    };

    [Theory]
    [InlineData(AnchorEdges.Left | AnchorEdges.Top, true)]
    [InlineData(AnchorEdges.Right | AnchorEdges.Bottom, true)]
    [InlineData(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top | AnchorEdges.Bottom, true)]
    [InlineData(AnchorEdges.Left, false)]
    [InlineData(AnchorEdges.Top | AnchorEdges.Bottom, false)]
    [InlineData(AnchorEdges.None, false)]
    [InlineData((AnchorEdges)64 | AnchorEdges.Default, false)]
    public void AnchorNeedsAHorizontalAndAVerticalEdge(AnchorEdges anchor, bool valid)
    {
        Assert.Equal(valid, AnchorLayout.IsValid(anchor));
    }

    [Fact]
    public void DefaultAnchorKeepsTheControlWhereItIs()
    {
        var bounds = AnchorLayout.Resolve(Screen, Button(AnchorEdges.Default), 1000, 900);

        Assert.Equal(new ControlBounds(100, 50, 120, 30), bounds);
    }

    [Fact]
    public void RightAndBottomAnchorsMoveTheControlWithThoseEdges()
    {
        var bounds = AnchorLayout.Resolve(Screen, Button(AnchorEdges.Right | AnchorEdges.Bottom), 1000, 900);

        // Grows by 200 × 300: the control keeps its distance from the right and bottom edges.
        Assert.Equal(new ControlBounds(300, 350, 120, 30), bounds);
    }

    [Fact]
    public void OppositeAnchorsStretchTheControl()
    {
        var bounds = AnchorLayout.Resolve(Screen, Button(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top), 1000, 900);

        Assert.Equal(new ControlBounds(100, 50, 320, 30), bounds);
    }

    [Fact]
    public void AtTheDesignSizeEveryAnchorGivesTheDesignedBounds()
    {
        foreach (var anchor in new[] { AnchorEdges.Default, AnchorEdges.Right | AnchorEdges.Bottom, AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top | AnchorEdges.Bottom })
        {
            Assert.Equal(new ControlBounds(100, 50, 120, 30), AnchorLayout.Resolve(Screen, Button(anchor), 800, 600));
        }
    }

    [Fact]
    public void PlacementGivesAlignmentMarginsAndFixedSizes()
    {
        var stretch = AnchorLayout.Place(Screen, Button(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Bottom));

        Assert.Equal(new AnchoredPlacement(AxisAlignment.Stretch, AxisAlignment.End, 100, 50, 580, 520, null, 30), stretch);
    }

    [Fact]
    public void ScreenIsResizableOnlyWhenSomethingFollowsTheRightOrBottomEdge()
    {
        Assert.False(AnchorLayout.IsResizable(Screen with { Controls = [Button(AnchorEdges.Default)] }));
        Assert.True(AnchorLayout.IsResizable(Screen with { Controls = [Button(AnchorEdges.Default), Button(AnchorEdges.Right | AnchorEdges.Top)] }));
    }

    [Fact]
    public void EditorRejectsAnAnchorWithoutBothAxes()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 0, 0);
        var before = editor.Document;

        Assert.NotNull(editor.SetAnchor(button.Id, AnchorEdges.Top));
        Assert.Same(before, editor.Document);

        Assert.Null(editor.SetAnchor(button.Id, AnchorEdges.Right | AnchorEdges.Top));
        Assert.Equal(AnchorEdges.Right | AnchorEdges.Top, editor.FindControl(button.Id)!.Anchor);

        editor.Undo();
        Assert.Equal(AnchorEdges.Default, editor.FindControl(button.Id)!.Anchor);
    }

    [Fact]
    public void AnchorsAreSavedAsEdgeNamesAndRoundTrip()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 0, 0);
        editor.SetAnchor(button.Id, AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Bottom);

        var json = ProjectFile.Serialize(editor.Document);
        var reloaded = ProjectFile.Deserialize(json);

        Assert.Contains("\"anchor\": [", json);
        Assert.Contains("\"left\"", json);
        Assert.Contains("\"bottom\"", json);
        Assert.Equal(AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Bottom, reloaded.Screen.Controls.Single().Anchor);
    }

    [Fact]
    public void VersionOneFilesLoadWithDefaultAnchorsAndSaveAsTheCurrentVersion()
    {
        var json = """
            {
              "schemaVersion": 1,
              "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
              "name": "Old",
              "screen": {
                "controls": [
                  { "id": "9e651cb8-84b8-4140-a171-62075666768e", "type": "Button", "name": "Go",
                    "x": 10, "y": 10, "width": 100, "height": 30, "properties": { "text": "Go" } }
                ]
              }
            }
            """;

        var document = ProjectFile.Deserialize(json);

        Assert.Equal(AnchorEdges.Default, document.Screen.Controls.Single().Anchor);
        Assert.Contains($"\"schemaVersion\": {ProjectDocument.CurrentSchemaVersion}", ProjectFile.Serialize(document));
    }

    [Theory]
    [InlineData("[\"left\"]", "Anchor to at least one")]
    [InlineData("[\"left\", \"middle\"]", "middle")]
    [InlineData("\"left\"", "not a valid project")]
    public void InvalidAnchorsAreRejectedOnLoad(string anchor, string expected)
    {
        var json = $$"""
            {
              "schemaVersion": 2,
              "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
              "screen": {
                "controls": [
                  { "id": "9e651cb8-84b8-4140-a171-62075666768e", "type": "Button", "name": "Go",
                    "x": 10, "y": 10, "width": 100, "height": 30, "anchor": {{anchor}} }
                ]
              }
            }
            """;

        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains(expected, ex.Message);
    }
}
