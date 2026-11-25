using System.IO;

namespace JellyfinDesktopSlideshow.Services;

public static class ImageFormatValidator
{
    private const int MinimumValidImageBytes = 64;

    public static bool TryDetectImageExtension(ReadOnlySpan<byte> data, out string extension)
    {
        extension = string.Empty;
        if (data.Length < MinimumValidImageBytes)
        {
            return false;
        }

        // JPEG: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            extension = ".jpg";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            extension = ".png";
            return true;
        }

        // WEBP: RIFF....WEBP
        if (data.Length >= 12 &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
        {
            extension = ".webp";
            return true;
        }

        // BMP: 42 4D
        if (data[0] == 0x42 && data[1] == 0x4D)
        {
            extension = ".bmp";
            return true;
        }

        return false;
    }

    public static bool IsValidImageFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < MinimumValidImageBytes)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[64];
            var read = stream.Read(header);
            return read >= 12 && TryDetectHeaderSignature(header[..read]);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDetectHeaderSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12)
        {
            return false;
        }

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true;
        }

        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return true;
        }

        if (header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return true;
        }

        return header[0] == 0x42 && header[1] == 0x4D;
    }
}
