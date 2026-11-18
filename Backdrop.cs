using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Orbit;

internal static class Backdrop
{
    // Blur once on a small CPU bitmap. No full-screen live Effect remains in the visual tree.
    public static BitmapSource? Create(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var source = new BitmapImage(); source.BeginInit(); source.UriSource = new Uri(path);
            source.CacheOption = BitmapCacheOption.OnLoad; source.DecodePixelWidth = 768; source.EndInit();
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h]; converted.CopyPixels(pixels, stride, 0);
            for (int pass = 0; pass < 3; pass++) { pixels = Blur(pixels, w, h, 4, true); pixels = Blur(pixels, w, h, 4, false); }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int offset = y * stride + x * 4;
                for (int c = 0; c < 3; c++)
                {
                    pixels[offset + c] = (byte)(pixels[offset + c] * .88);
                }
                pixels[offset + 3] = 255;
            }
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride); result.Freeze(); return result;
        }
        catch { return null; }
    }
    private static byte[] Blur(byte[] input, int w, int h, int radius, bool horizontal)
    {
        var output = new byte[input.Length]; int length = horizontal ? w : h, lines = horizontal ? h : w, count = radius * 2 + 1;
        for (int line = 0; line < lines; line++) for (int c = 0; c < 4; c++)
        {
            int Index(int position) => horizontal ? (line * w + Math.Clamp(position, 0, w - 1)) * 4 + c : (Math.Clamp(position, 0, h - 1) * w + line) * 4 + c;
            int sum = 0; for (int k = -radius; k <= radius; k++) sum += input[Index(k)];
            for (int position = 0; position < length; position++)
            {
                output[Index(position)] = (byte)(sum / count);
                sum += input[Index(position + radius + 1)] - input[Index(position - radius)];
            }
        }
        return output;
    }
}
