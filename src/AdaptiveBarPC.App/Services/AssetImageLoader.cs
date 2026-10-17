using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AdaptiveBarPC.Services;

public static class AssetImageLoader
{
    public static ImageSource? LoadIcon(AssetPathResolver paths, string? reference)
    {
        var path = paths.ResolveExistingFile(reference);
        if (path is null)
            return null;

        return LoadBitmapFile(path, 32);
    }

    public static ImageSource? LoadBitmapFile(string path, int decodePixelWidth)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
                image.DecodePixelWidth = decodePixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
