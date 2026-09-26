namespace StandaloneUiBuilder.Core.Tests;

public class LibraryEditingTests
{
    [Fact]
    public void ALibraryNeedsAPlatform()
    {
        var editor = new DesignEditor();
        Assert.Equal("Choose the project's platform first (Project > Platform): a library's controls exist on one platform only.",
            editor.AddLibrary(SampleLibrary.Package(ProjectPlatform.Wpf)));
        Assert.Null(editor.Document.Libraries);
    }

    [Fact]
    public void ALibraryControlIsPlacedLikeAnyOther()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var rating = editor.AddLibraryControl("SampleControls.Wpf.Rating", 37, 44)!;
        Assert.Equal(ControlType.Custom, rating.Type);
        Assert.Equal("Rating1", rating.Name);
        Assert.Equal((40, 40, 160, 32), (rating.X, rating.Y, rating.Width, rating.Height));
        Assert.Equal("SampleControls.Wpf.Rating", rating.Properties.LibraryType);
        Assert.Equal("StandaloneUiBuilder.SampleControls", rating.Properties.LibraryAssembly);
        Assert.Null(rating.Properties.LibrarySettings);
        Assert.Equal("Rating2", editor.AddLibraryControl("SampleControls.Wpf.Rating", 200, 200)!.Name);
        Assert.Null(editor.AddLibraryControl("SampleControls.Wpf.Unknown", 0, 0));

        var stack = editor.AddControl(ControlType.StackPanel, 300, 300);
        var inside = editor.AddLibraryControlTo("SampleControls.Wpf.Badge", stack.Id, 310, 310)!;
        Assert.Equal(stack.Id, editor.ParentOf(inside.Id)!.Id);
    }

    [Fact]
    public void ValuesAreCheckedAndStoredWithTheirType()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var id = editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0)!.Id;
        Assert.Null(editor.SetLibrarySetting(id, "Stars", " 7 "));
        Assert.Null(editor.SetLibrarySetting(id, "Shape", "heart"));
        Assert.Null(editor.SetLibrarySetting(id, "ShowValue", "true"));
        Assert.Null(editor.SetLibrarySetting(id, "Caption", " Rate us "));
        Assert.Null(editor.SetLibrarySetting(id, "Value", "2.5"));
        Assert.Equal("Stars must be a whole number.", editor.SetLibrarySetting(id, "Stars", "many"));
        Assert.Equal("Stars must be a whole number.", editor.SetLibrarySetting(id, "Stars", "9999999999"));
        Assert.Equal("Shape must be one of: Star, Heart, Circle.", editor.SetLibrarySetting(id, "Shape", "Square"));
        Assert.Equal("ShowValue is on or off: True or False.", editor.SetLibrarySetting(id, "ShowValue", "maybe"));
        Assert.Equal("Rating1 has no property Colour.", editor.SetLibrarySetting(id, "Colour", "Red"));

        // Kept in the order the inspector lists the properties.
        Assert.Equal(
            [("Caption", "System.String", " Rate us "), ("Stars", "System.Int32", "7"), ("Value", "System.Double", "2.5"),
             ("ShowValue", "System.Boolean", "True"), ("Shape", "SampleControls.Wpf.RatingShape", "Heart")],
            editor.FindControl(id)!.Properties.LibrarySettings!.Select(s => (s.Name, s.Type, s.Value)));

        // Empty goes back to the library's default; Undo puts the value back.
        Assert.Null(editor.SetLibrarySetting(id, "Stars", ""));
        Assert.DoesNotContain(editor.FindControl(id)!.Properties.LibrarySettings!, s => s.Name == "Stars");
        editor.Undo();
        Assert.Contains(editor.FindControl(id)!.Properties.LibrarySettings!, s => s.Name == "Stars");
    }

    [Fact]
    public void AGenericComponentStartsWithATypeArgument()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Blazor);
        var picker = editor.AddLibraryControl("SampleControls.Blazor.Picker", 0, 0)!;
        var argument = Assert.Single(picker.Properties.LibrarySettings!);
        Assert.Equal(("TValue", "System.Type", "string"), (argument.Name, argument.Type, argument.Value));
    }

    [Fact]
    public void ALibraryInUseCannotBeRemoved()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var rating = editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0)!;
        Assert.Equal("Sample.Controls is still used by Rating1. Delete them first.", editor.RemoveLibrary("sample.controls"));
        editor.DeleteControls([rating.Id]);
        Assert.Null(editor.RemoveLibrary(SampleLibrary.PackageId));
        Assert.Null(editor.Document.Libraries);
        Assert.Equal("The project does not use Sample.Controls.", editor.RemoveLibrary(SampleLibrary.PackageId));
    }

    [Fact]
    public void UpdatingALibraryDropsValuesItNoLongerHas()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var id = editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0)!.Id;
        editor.SetLibrarySetting(id, "Stars", "3");
        editor.SetLibrarySetting(id, "Caption", "Hi");
        var older = SampleLibrary.Package(ProjectPlatform.Wpf, "2.0.0");
        older = older with
        {
            Controls = older.Controls.ConvertAll(c => c.Name == "Rating" ? c with { Properties = c.Properties.RemoveAll(p => p.Name == "Stars") } : c),
        };
        Assert.Null(editor.AddLibrary(older));
        Assert.Equal("2.0.0", Assert.Single(editor.Document.Libraries!).Version);
        Assert.Equal(["Caption"], editor.FindControl(id)!.Properties.LibrarySettings!.Select(s => s.Name));
    }

    [Fact]
    public void ChangingThePlatformDropsLibrariesUntilUndone()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var stack = editor.AddControl(ControlType.StackPanel, 100, 100);
        editor.AddLibraryControlTo("SampleControls.Wpf.Badge", stack.Id, 110, 110);
        editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0);
        Assert.Equal(2, editor.LibraryControlCount);

        Assert.True(editor.SetPlatform(ProjectPlatform.Blazor));
        Assert.Null(editor.Document.Libraries);
        Assert.Equal(0, editor.LibraryControlCount);
        Assert.Empty(editor.FindControl(stack.Id)!.Children!);

        editor.Undo();
        Assert.Equal(ProjectPlatform.Wpf, editor.Document.Platform);
        Assert.Equal(2, editor.LibraryControlCount);
    }

    [Fact]
    public void PastingKeepsLibraryControlsOnlyWhereTheLibraryIs()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var rating = editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0)!;
        var pasted = Assert.Single(editor.PasteControls([rating], 20));
        Assert.Equal("Rating2", pasted.Name);

        var other = new DesignEditor();
        other.New(ProjectPlatform.Wpf);
        Assert.Empty(other.PasteControls([rating], 20));
    }

    [Fact]
    public void LibrariesAndValuesAreSavedAndRead()
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var id = editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0)!.Id;
        editor.SetLibrarySetting(id, "Shape", "Circle");
        editor.SetLicenseKey("Syncfusion", "  key-123 ");

        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains("\"libraryType\": \"SampleControls.Wpf.Rating\"", json);
        var read = ProjectFile.Deserialize(json);
        Assert.Equal(SampleLibrary.PackageId, Assert.Single(read.Libraries!).Id);
        Assert.Equal(3, read.Libraries![0].Controls.Count);
        Assert.Equal("key-123", read.LicenseKeys!["Syncfusion"]);
        var control = Assert.Single(read.MainScreen.Controls);
        Assert.Equal("Circle", Assert.Single(control.Properties.LibrarySettings!).Value);
    }

    [Theory]
    [InlineData("\"value\": \"Circle\"", "\"value\": \"Circle\\\" onload=\\\"x\"", "the value of \"Shape\" is not valid")]
    [InlineData("\"libraryType\": \"SampleControls.Wpf.Rating\"", "\"libraryType\": \"Sample Controls<script>\"", "does not name a valid library control type")]
    [InlineData("\"platform\": \"Wpf\"", "\"platform\": \"Any\"", "a library needs a platform")]
    [InlineData("\"version\": \"1.2.0\"", "\"version\": \"1.2.0\\\" Condition=\\\"\"", "does not have a valid package ID and version")]
    public void HandEditedLibraryDataThatWouldReachCodeIsRejected(string find, string replace, string message)
    {
        var editor = SampleLibrary.Editor(ProjectPlatform.Wpf);
        var id = editor.AddLibraryControl("SampleControls.Wpf.Rating", 0, 0)!.Id;
        editor.SetLibrarySetting(id, "Shape", "Circle");
        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains(find, json);
        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json.Replace(find, replace, StringComparison.Ordinal)));
        Assert.Contains(message, error.Message);
    }
}
