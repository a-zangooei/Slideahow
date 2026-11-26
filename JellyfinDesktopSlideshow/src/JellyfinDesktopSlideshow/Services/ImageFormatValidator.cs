using System.Buffers.Binary;
using System.IO;

namespace JellyfinDesktopSlideshow.Services;

public static class ImageFormatValidator
{
    private const int MinimumValidImageBytes = 64;
    private const int MaxHeaderScanBytes = 65536;

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

    public static bool TryReadImageDimensions(string? filePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var toRead = (int)Math.Min(stream.Length, MaxHeaderScanBytes);
            if (toRead < 16)
            {
                return false;
            }

            var buffer = new byte[toRead];
            var totalRead = 0;
            while (totalRead < toRead)
            {
                var read = stream.Read(buffer, totalRead, toRead - totalRead);
                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }

            return TryReadImageDimensions(buffer.AsSpan(0, totalRead), out width, out height);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadImageDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 16)
        {
            return false;
        }

        // PNG: 8-byte signature + length(4) + "IHDR"(4) + width(4) + height(4)
        if (data.Length >= 24 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A &&
            data[12] == (byte)'I' && data[13] == (byte)'H' && data[14] == (byte)'D' && data[15] == (byte)'R')
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
            return width > 0 && height > 0;
        }

        // BMP: "BM" + header(14) + DIB header
        if (data.Length >= 26 && data[0] == 0x42 && data[1] == 0x4D)
        {
            var dibSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(14, 4));
            if (dibSize == 12)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2));
                height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(20, 2));
                return width > 0 && height > 0;
            }

            if (dibSize >= 16)
            {
                width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
                var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
                height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
                return width > 0 && height > 0;
            }
        }

        // WEBP: RIFF....WEBP + chunk FourCC
        if (data.Length >= 25 &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
        {
            if (data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)' ' &&
                data.Length >= 30 &&
                data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
                return width > 0 && height > 0;
            }

            if (data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)'L' &&
                data[20] == 0x2F &&
                data.Length >= 25)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                return width > 0 && height > 0;
            }

            if (data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)'X' &&
                data.Length >= 30)
            {
                width = (data[24] | (data[25] << 8) | (data[26] << 16)) + 1;
                height = (data[27] | (data[28] << 8) | (data[29] << 16)) + 1;
                return width > 0 && height > 0;
            }
        }

        // JPEG: FF D8 ... SOF marker
        if (data[0] == 0xFF && data[1] == 0xD8)
        {
            return TryReadJpegDimensions(data, out width, out height);
        }

        return false;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        var index = 2;
        while (index + 1 < data.Length)
        {
            while (index < data.Length && data[index] != 0xFF)
            {
                index++;
            }

            while (index < data.Length && data[index] == 0xFF)
            {
                index++;
            }

            if (index >= data.Length)
            {
                break;
            }

            var marker = data[index++];
            if (marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
            {
                continue;
            }

            if (marker == 0xD9 || marker == 0xDA)
            {
                break;
            }

            if (index + 2 > data.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index, 2));
            if (segmentLength < 2 || index + segmentLength > data.Length)
            {
                break;
            }

            var isSofWithDimensions =
                (marker >= 0xC0 && marker <= 0xC3) ||
                (marker >= 0xC5 && marker <= 0xC7) ||
                (marker >= 0xC9 && marker <= 0xCB) ||
                (marker >= 0xCD && marker <= 0xCF);

            if (isSofWithDimensions && index + 7 <= data.Length)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 5, 2));
                return width > 0 && height > 0;
            }

            index += segmentLength;
        }

        return false;
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
