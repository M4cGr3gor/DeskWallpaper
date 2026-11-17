using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace DeskWallpaper.App.Vistas;

/// <summary>Bytes de imagen (PNG o JPEG) a <see cref="BitmapImage"/> para mostrarlos en pantalla.</summary>
internal static class ImagenesPng
{
    public static async Task<BitmapImage> CargarAsync(byte[] datos)
    {
        using var flujo = new InMemoryRandomAccessStream();
        await flujo.WriteAsync(datos.AsBuffer());
        flujo.Seek(0);
        var imagen = new BitmapImage();
        await imagen.SetSourceAsync(flujo);
        return imagen;
    }
}
