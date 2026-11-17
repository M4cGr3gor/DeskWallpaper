#:package SkiaSharp@4.155.0
// Genera el icono de DeskWallpaper (ICO multitamaño con PNG dentro) y una vista previa PNG.
// Uso: dotnet run tools/GenerarIcono.cs -- <carpeta de salida>
using SkiaSharp;

#pragma warning disable CS0618 // SKPath.MoveTo/LineTo: suficiente para un script de una sola vez

var salida = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(salida);
int[] tamanos = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

var pngs = tamanos.Select(t => (Tamano: t, Datos: Dibujar(t))).ToList();
File.WriteAllBytes(Path.Combine(salida, "DeskWallpaper.ico"), Ico(pngs));
File.WriteAllBytes(Path.Combine(salida, "DeskWallpaper-256.png"), pngs.Last().Datos);
Console.WriteLine($"Icono generado en {Path.GetFullPath(salida)}");

static byte[] Dibujar(int t)
{
    using var sup = SKSurface.Create(new SKImageInfo(t, t, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = sup.Canvas;
    c.Clear(SKColors.Transparent);
    c.Scale(t, t); // se dibuja en coordenadas 0..1
    var detalle = t >= 32;

    // Pantallas (coordenadas 0..1): dos arriba juntas y una más pequeña centrada debajo.
    var izq = new SKRect(0.03f, 0.10f, 0.485f, 0.40f);
    var der = new SKRect(0.515f, 0.10f, 0.97f, 0.40f);
    var abajo = new SKRect(0.29f, 0.46f, 0.71f, 0.725f);
    var radio = 0.035f;
    var borde = t <= 20 ? 0.045f : 0.03f; // marco de la pantalla

    using var marco = new SKPaint { Color = new SKColor(0x2B, 0x2F, 0x36), IsAntialias = true };
    using var contorno = new SKPaint { Color = new SKColor(0x8A, 0x92, 0x9C), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f / t };
    foreach (var r in new[] { izq, der, abajo })
    {
        c.DrawRoundRect(r, radio, radio, marco);
        c.DrawRoundRect(r, radio, radio, contorno);
    }

    // Panorámica de atardecer que continúa de una pantalla a otra (el marco central la "tapa").
    var panoramica = new SKRect(izq.Left + borde, izq.Top + borde, der.Right - borde, izq.Bottom - borde);
    using var cielo = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, panoramica.Top), new SKPoint(0, panoramica.Bottom),
            [new SKColor(0x3A, 0x3F, 0x9E), new SKColor(0xE0, 0x56, 0x9B), new SKColor(0xFF, 0xB3, 0x47)], [0f, 0.55f, 1f], SKShaderTileMode.Clamp),
    };
    using var sol = new SKPaint { Color = new SKColor(0xFF, 0xE8, 0xA8), IsAntialias = true };
    using var montes = new SKPaint { Color = new SKColor(0x3B, 0x23, 0x57), IsAntialias = true };
    using var montaña = new SKPath();
    montaña.MoveTo(panoramica.Left, panoramica.Bottom);
    foreach (var (x, y) in new[] { (0.03f, 0.33f), (0.16f, 0.25f), (0.29f, 0.32f), (0.44f, 0.22f), (0.60f, 0.33f), (0.76f, 0.26f), (0.97f, 0.34f) })
        montaña.LineTo(x, y);
    montaña.LineTo(panoramica.Right, panoramica.Bottom);
    montaña.Close();

    foreach (var pantalla in new[] { izq, der })
    {
        var interior = new SKRect(pantalla.Left + borde, pantalla.Top + borde, pantalla.Right - borde, pantalla.Bottom - borde);
        c.Save();
        c.ClipRoundRect(new SKRoundRect(interior, radio * 0.4f), antialias: true);
        c.DrawRect(panoramica, cielo);
        c.DrawCircle(0.62f, 0.24f, 0.065f, sol);
        c.DrawPath(montaña, montes);
        c.Restore();
    }

    // Pantalla de abajo: cielo nocturno con luna.
    var interiorAbajo = new SKRect(abajo.Left + borde, abajo.Top + borde, abajo.Right - borde, abajo.Bottom - borde);
    using var noche = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, interiorAbajo.Top), new SKPoint(0, interiorAbajo.Bottom),
            [new SKColor(0x0B, 0x1E, 0x3F), new SKColor(0x2A, 0x5C, 0xA8)], SKShaderTileMode.Clamp),
    };
    using var luna = new SKPaint { Color = new SKColor(0xF2, 0xF4, 0xF8), IsAntialias = true };
    c.Save();
    c.ClipRoundRect(new SKRoundRect(interiorAbajo, radio * 0.4f), antialias: true);
    c.DrawRect(interiorAbajo, noche);
    c.DrawCircle(0.585f, 0.545f, 0.04f, luna);
    if (detalle)
        foreach (var (x, y) in new[] { (0.36f, 0.52f), (0.43f, 0.6f), (0.50f, 0.51f), (0.40f, 0.66f), (0.64f, 0.64f) })
            c.DrawCircle(x, y, 0.007f, luna);
    c.Restore();

    // Peana común: refuerza la idea de "escritorio" a tamaños grandes.
    if (detalle)
    {
        using var peana = new SKPaint { Color = new SKColor(0x6B, 0x72, 0x7C), IsAntialias = true };
        c.DrawRect(new SKRect(0.475f, 0.725f, 0.525f, 0.84f), peana);
        c.DrawRoundRect(new SKRect(0.36f, 0.83f, 0.64f, 0.875f), 0.02f, 0.02f, peana);
    }

    using var img = sup.Snapshot();
    using var datos = img.Encode(SKEncodedImageFormat.Png, 100);
    return datos.ToArray();
}

// Los tamaños menores de 256 van como DIB de 32 bits (formato clásico, lo entiende todo Windows);
// el de 256 va como PNG, igual que en los iconos del sistema.
static byte[] Dib(byte[] png, int t)
{
    using var original = SKBitmap.Decode(png);
    using var px = new SKBitmap(new SKImageInfo(t, t, SKColorType.Bgra8888, SKAlphaType.Unpremul));
    using (var lienzo = new SKCanvas(px)) lienzo.DrawBitmap(original, 0, 0);

    var filaMascara = (t + 31) / 32 * 4;
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40); w.Write(t); w.Write(t * 2); w.Write((ushort)1); w.Write((ushort)32);
    w.Write(0); w.Write(t * t * 4 + filaMascara * t); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    for (var y = t - 1; y >= 0; y--) // de abajo arriba
        for (var x = 0; x < t; x++)
        {
            var c = px.GetPixel(x, y);
            w.Write(c.Blue); w.Write(c.Green); w.Write(c.Red); w.Write(c.Alpha);
        }
    w.Write(new byte[filaMascara * t]); // máscara AND vacía: manda el canal alfa
    return ms.ToArray();
}

static byte[] Ico(List<(int Tamano, byte[] Datos)> imagenes)
{
    imagenes = imagenes.Select(i => (i.Tamano, i.Tamano >= 256 ? i.Datos : Dib(i.Datos, i.Tamano))).ToList();
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)imagenes.Count);
    var desplazamiento = 6 + 16 * imagenes.Count;
    foreach (var (t, d) in imagenes)
    {
        w.Write((byte)(t >= 256 ? 0 : t)); w.Write((byte)(t >= 256 ? 0 : t));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write((uint)d.Length); w.Write((uint)desplazamiento);
        desplazamiento += d.Length;
    }
    foreach (var (_, d) in imagenes) w.Write(d);
    return ms.ToArray();
}
