namespace StandaloneUiBuilder.Core;

/// <summary>
/// The picture files an Image can hold: PNG, JPEG, GIF and BMP, which WPF, WinForms and every
/// browser display. Recognised by their first bytes, not their file name.
/// </summary>
public static class ImageFile
{
    /// <summary>The largest picture a project stores, so projects stay quick to open and save.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const string OpenFilter = "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp";

    /// <summary>The usual file extension for a picture's bytes, such as ".png", or null if it is not one.</summary>
    public static string? Extension(ReadOnlySpan<byte> data) =>
        data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? ".png"
        : data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? ".jpg"
        : data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8) ? ".gif"
        : data.StartsWith("BM"u8) && data.Length > 14 ? ".bmp"
        : null;

    /// <summary>Checks a picture's bytes; returns an error message or null.</summary>
    public static string? Validate(byte[] data)
    {
        if (data.Length > MaxBytes)
        {
            return $"The picture is {data.Length / 1024 / 1024.0:0.#} MB; pictures can be at most {MaxBytes / 1024 / 1024} MB.";
        }

        return Extension(data) is null ? "The file is not a PNG, JPEG, GIF or BMP picture." : null;
    }

    public static bool TryDecode(string base64, out byte[] data)
    {
        data = [];
        var buffer = new byte[base64.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(base64, buffer, out var written))
        {
            return false;
        }

        data = buffer[..written];
        return true;
    }

    /// <summary>
    /// Where an exported project keeps an Image's picture: a folder per screen, the file named
    /// after the control, for example <c>Assets/Main/Logo.png</c>.
    /// </summary>
    public static string ExportPath(ScreenDocument screen, ControlDocument image) =>
        $"Assets/{screen.Name}/{image.Name}{(image.Properties.ImageData is { } data && TryDecode(data, out var bytes) ? Extension(bytes) : null) ?? ".png"}";
}
