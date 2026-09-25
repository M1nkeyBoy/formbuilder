using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.UiTests;

/// <summary>Where an Image's picture appears: fitted into the designed box, keeping its shape, centred.</summary>
internal static class PictureFit
{
    /// <summary>
    /// The box the picture of a "Uniform" Image covers, or the designed box itself for a
    /// stretched Image or one without a picture. Reads the size of PNG pictures.
    /// </summary>
    public static ControlBounds Expected(ControlDocument control, ControlBounds designed)
    {
        var properties = control.Properties;
        if (control.Type != Core.ControlType.Image || properties.Stretch == ImageStretch.Fill
            || properties.ImageData is not { } data || !ImageFile.TryDecode(data, out var bytes) || ImageFile.Extension(bytes) != ".png")
        {
            return designed;
        }

        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        var scale = Math.Min(designed.Width / (double)width, designed.Height / (double)height);
        var fittedWidth = width * scale;
        var fittedHeight = height * scale;
        return new ControlBounds(
            (int)Math.Round(designed.X + (designed.Width - fittedWidth) / 2),
            (int)Math.Round(designed.Y + (designed.Height - fittedHeight) / 2),
            (int)Math.Round(fittedWidth),
            (int)Math.Round(fittedHeight));
    }
}
