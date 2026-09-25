using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

public class ImageTests
{
    // The smallest valid PNG header is enough: pictures are recognised by their first bytes.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private static (DesignEditor Editor, ControlDocument Image) WithPicture()
    {
        var editor = new DesignEditor();
        var image = editor.AddControl(ControlType.Image, 10, 10);
        editor.Rename(image.Id, "Logo");
        editor.SetImage(image.Id, Png);
        return (editor, editor.FindControl(image.Id)!);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, ".gif")]
    [InlineData(new byte[] { (byte)'<', (byte)'s', (byte)'v', (byte)'g' }, null)]
    public void PicturesAreRecognisedByTheirFirstBytes(byte[] data, string? extension) =>
        Assert.Equal(extension, ImageFile.Extension(data));

    [Fact]
    public void AnImageStoresItsPictureInTheProject()
    {
        var (editor, image) = WithPicture();

        Assert.Equal(Convert.ToBase64String(Png), image.Properties.ImageData);
        Assert.Equal(ImageStretch.Uniform, image.Properties.Stretch);
        var reloaded = ProjectFile.Deserialize(ProjectFile.Serialize(editor.Document));
        Assert.Equal(image.Properties.ImageData, reloaded.MainScreen.Controls[0].Properties.ImageData);

        editor.SetImage(image.Id, null);
        Assert.Null(editor.FindControl(image.Id)!.Properties.ImageData);
    }

    [Fact]
    public void OnlyPicturesOfAllowedSizeAndKindAreTaken()
    {
        var (editor, image) = WithPicture();

        Assert.Equal("The file is not a PNG, JPEG, GIF or BMP picture.", editor.SetImage(image.Id, "not a picture"u8.ToArray()));
        var huge = new byte[ImageFile.MaxBytes + 1];
        Png.CopyTo(huge, 0);
        Assert.Contains("at most 2 MB", editor.SetImage(image.Id, huge));
        Assert.Equal("A Image does not have a background colour.", editor.SetColors(image.Id, null, "#FFFFFF"));
    }

    [Fact]
    public void ABrokenPictureIsRejectedOnLoad()
    {
        var (editor, _) = WithPicture();
        var json = ProjectFile.Serialize(editor.Document).Replace(Convert.ToBase64String(Png), "!!!", StringComparison.Ordinal);

        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains("Control \"Logo\": The picture data is not valid base64.", error.Message);
    }

    [Fact]
    public void EachTargetShipsThePictureAndShowsIt()
    {
        var (editor, image) = WithPicture();
        editor.SetImageStretch(image.Id, ImageStretch.Fill);
        var document = editor.Document;

        var wpf = WpfGenerator.Generate(document, "T").ToDictionary(f => f.RelativePath);
        Assert.Equal(Png, wpf["Assets/Main/Logo.png"].Bytes);
        Assert.Contains("<Image x:Name=\"Logo\"", wpf["MainWindow.xaml"].Content);
        Assert.Contains("Source=\"Assets/Main/Logo.png\" Stretch=\"Fill\"", wpf["MainWindow.xaml"].Content);
        Assert.Contains("<Resource Include=\"Assets\\**\" />", wpf["T.csproj"].Content);

        var winForms = WinFormsGenerator.Generate(document, "T").ToDictionary(f => f.RelativePath);
        Assert.Equal(Png, winForms["Assets/Main/Logo.png"].Bytes);
        Assert.Contains("this.Logo = new System.Windows.Forms.PictureBox();", winForms["MainForm.Designer.cs"].Content);
        Assert.Contains("this.Logo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;", winForms["MainForm.Designer.cs"].Content);
        Assert.Contains("this.Logo.Image = System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, \"Assets\", \"Main\", \"Logo.png\"));", winForms["MainForm.Designer.cs"].Content);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", winForms["T.csproj"].Content);

        var blazor = BlazorGenerator.Generate(document, "T").ToDictionary(f => f.RelativePath);
        Assert.Equal(Png, blazor["wwwroot/Assets/Main/Logo.png"].Bytes);
        Assert.Contains("object-fit:fill\" src=\"Assets/Main/Logo.png\" alt=\"\" class=\"uib-image\" />", blazor["Components/Pages/MainPage.razor"].Content);
    }

    [Fact]
    public void AnImageWithoutAPictureShipsNothing()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Image, 10, 10);

        Assert.DoesNotContain(WpfGenerator.Generate(editor.Document, "T"), f => f.Bytes is not null);
        Assert.DoesNotContain("Source=", WpfGenerator.WindowXaml(editor.Document, "T"));
        Assert.DoesNotContain(".Image = ", WinFormsGenerator.DesignerCode(editor.Document, "T"));
    }

    [Fact]
    public void ExportWritesThePictureFile()
    {
        var (editor, _) = WithPicture();
        var parent = Directory.CreateTempSubdirectory("uib-image-").FullName;
        try
        {
            var result = WpfExporter.Export(editor.Document with { Name = "Pictures" }, parent);

            Assert.Equal(Png, File.ReadAllBytes(Path.Combine(result.ProjectFolder, "Assets", "Main", "Logo.png")));
            Assert.Contains("Assets/Main/Logo.png", WpfExporter.Export(editor.Document with { Name = "Pictures" }, parent).Kept);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
