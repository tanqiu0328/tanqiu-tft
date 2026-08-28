using System.IO;
using System.Windows.Media.Imaging;

namespace TanqiuTft.App;

internal static class BitmapImageLoader
{
    public static BitmapImage Load(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        return Load(image => image.StreamSource = stream);
    }

    public static BitmapImage Load(string imagePath)
    {
        return Load(image => image.UriSource = new Uri(imagePath, UriKind.Absolute));
    }

    private static BitmapImage Load(Action<BitmapImage> setSource)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        setSource(image);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
