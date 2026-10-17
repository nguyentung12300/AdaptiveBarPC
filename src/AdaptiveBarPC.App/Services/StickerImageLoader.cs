using System.IO;
using System.Windows.Media.Imaging;

namespace AdaptiveBarPC.Services;

public static class StickerImageLoader
{
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private const long MaxPixels = 16 * 1024 * 1024;

    public static BitmapSource? Load(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || !IsSupportedExtension(path) || file.Length > MaxFileBytes)
                return null;

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnDemand);
            if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder || decoder.Frames.Count == 0)
                return null;

            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                (long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
                return null;

            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            if (decodePixelWidth > 0)
                image.DecodePixelWidth = decodePixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsSupportedExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
}
