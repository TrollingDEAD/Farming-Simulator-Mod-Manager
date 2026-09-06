using System.IO;
using System.Windows.Media.Imaging;

namespace FsModManager.App.Services;

/// <summary>
/// Shared byte[] -&gt; BitmapSource decode helper for anything Core hands back as raw image bytes
/// (mod icons, per-storeItem shop images) — Core stays UI-framework-agnostic and every call site
/// gets the same never-throws behavior instead of duplicating this pattern.
/// </summary>
internal static class ImageDecoding
{
    /// <summary>Decodes the given bytes to a frozen, UI-thread-safe BitmapSource, or null on any failure.</summary>
    public static BitmapSource? TryDecodeToBitmapSource(byte[]? imageData)
    {
        if (imageData is null || imageData.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(imageData);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
