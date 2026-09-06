using Pfim;

namespace FsModManager.Core.ModScanning.Parsing;

/// <summary>
/// Decodes DDS texture bytes (the common format for FS mod icons) into a self-contained BMP byte
/// buffer that any UI framework can hand straight to its native image decoder (e.g. WPF's
/// BitmapImage) without taking a dependency on Pfim itself, keeping Core UI-framework-agnostic.
/// </summary>
internal static class DdsIconDecoder
{
    /// <summary>
    /// Returns BMP-encoded bytes for the given DDS image, or null if the bytes aren't a DDS,
    /// decode to a pixel format this decoder doesn't handle, or anything else goes wrong.
    /// Never throws.
    /// </summary>
    public static byte[]? TryDecodeToBmp(byte[] ddsBytes)
    {
        try
        {
            using var stream = new MemoryStream(ddsBytes);
            using var image = Pfimage.FromStream(stream);

            // Pfim decompresses DXT/BCn textures for us; we only need to handle the two raw
            // pixel layouts it commonly produces for FS icon DDS files. Anything else (paletted
            // formats etc.) is rare enough here that we bail out rather than risk a bad decode.
            return image.Format switch
            {
                ImageFormat.Rgba32 => EncodeBmp32(image.Data, image.Width, image.Height, image.Stride),
                ImageFormat.Rgb24 => EncodeBmp24(image.Data, image.Width, image.Height, image.Stride),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    // Pfim's "Rgba32" format is actually laid out as BGRA in memory (a well-known Pfim quirk),
    // which conveniently matches the byte order 32bpp BMP pixels use, so this is a straight copy.
    private static byte[] EncodeBmp32(byte[] pixels, int width, int height, int stride)
    {
        const int headerSize = 14 + 40;
        var rowSize = width * 4;
        var buffer = new byte[headerSize + rowSize * height];

        WriteHeaders(buffer, width, height, bitsPerPixel: 32, imageDataSize: rowSize * height);

        CopyRows(pixels, stride, buffer, headerSize, rowSize, height);
        return buffer;
    }

    // 24bpp BGR, no alpha. BMP rows are padded to a 4-byte boundary.
    private static byte[] EncodeBmp24(byte[] pixels, int width, int height, int stride)
    {
        const int headerSize = 14 + 40;
        var rowSize = ((width * 3 + 3) / 4) * 4;
        var buffer = new byte[headerSize + rowSize * height];

        WriteHeaders(buffer, width, height, bitsPerPixel: 24, imageDataSize: rowSize * height);

        CopyRows(pixels, stride, buffer, headerSize, rowSize, height);
        return buffer;
    }

    private static void CopyRows(byte[] source, int sourceStride, byte[] destination, int destinationOffset, int destinationStride, int height)
    {
        var copyLength = Math.Min(sourceStride, destinationStride);
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(source, row * sourceStride, destination, destinationOffset + row * destinationStride, copyLength);
        }
    }

    // Writes a BITMAPFILEHEADER + BITMAPINFOHEADER with a negative height, marking the DIB as
    // top-down so it matches Pfim's already top-down decoded buffer with no row-flipping needed.
    private static void WriteHeaders(byte[] buffer, int width, int height, int bitsPerPixel, int imageDataSize)
    {
        const int headerSize = 14 + 40;

        // BITMAPFILEHEADER
        buffer[0] = (byte)'B';
        buffer[1] = (byte)'M';
        WriteInt32(buffer, 2, headerSize + imageDataSize);
        WriteInt32(buffer, 10, headerSize);

        // BITMAPINFOHEADER
        WriteInt32(buffer, 14, 40);
        WriteInt32(buffer, 18, width);
        WriteInt32(buffer, 22, -height); // negative = top-down
        WriteInt16(buffer, 26, 1);
        WriteInt16(buffer, 28, (short)bitsPerPixel);
        WriteInt32(buffer, 30, 0); // BI_RGB, uncompressed
        WriteInt32(buffer, 34, imageDataSize);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), value);

    private static void WriteInt16(byte[] buffer, int offset, short value) =>
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, 2), value);
}
