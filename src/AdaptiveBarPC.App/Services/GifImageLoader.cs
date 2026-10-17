using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AdaptiveBarPC.Services;

internal static class GifImageLoader
{
    // GDI+ composites GIF disposal/transparency; only frozen WPF frames leave this worker.
    internal static (ObjectAnimationUsingKeyFrames Animation, System.Windows.Size Size)? Load(
        string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var gif = System.Drawing.Image.FromStream(stream);
            var size = new System.Windows.Size(gif.Width, gif.Height);
            // Preserve original resolution for cover/zoom, as the previous GIF renderer did.
            var width = gif.Width;
            var height = gif.Height;
            var count = gif.GetFrameCount(FrameDimension.Time);
            var delays = gif.PropertyIdList.Contains(0x5100) ? gif.GetPropertyItem(0x5100)?.Value : null;
            var animation = new ObjectAnimationUsingKeyFrames();
            var elapsed = TimeSpan.Zero;
            using var frame = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var graphics = System.Drawing.Graphics.FromImage(frame);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            var bounds = new System.Drawing.Rectangle(0, 0, width, height);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                gif.SelectActiveFrame(FrameDimension.Time, index);
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.DrawImage(gif, bounds);
                var data = frame.LockBits(bounds, ImageLockMode.ReadOnly, frame.PixelFormat);
                BitmapSource bitmap;
                try
                {
                    bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null,
                        data.Scan0, data.Stride * height, data.Stride);
                    bitmap.Freeze();
                }
                finally { frame.UnlockBits(data); }
                animation.KeyFrames.Add(new DiscreteObjectKeyFrame(bitmap, elapsed));
                var delay = delays is not null && delays.Length / 4 > index ? BitConverter.ToUInt32(delays, index * 4) : 0;
                elapsed += TimeSpan.FromMilliseconds(delay == 0 ? 100 : delay * 10d);
            }
            animation.Duration = elapsed;
            animation.Freeze();
            return (animation, size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                      or InvalidOperationException or NotSupportedException or ExternalException
                                      or OutOfMemoryException) // GDI+ also reports invalid image data as OutOfMemory.
        {
            return null;
        }
    }
}
