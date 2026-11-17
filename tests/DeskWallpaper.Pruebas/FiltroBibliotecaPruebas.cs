using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class FiltroBibliotecaPruebas
{
    private static readonly LienzoFisico Lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
    private static readonly IReadOnlyList<Zona> Zonas = ZonasPorDefecto.Calcular(Usuario);

    private static ImagenBiblioteca Img(long id, string nombre, int ancho, int alto, bool favorita = false,
                                        ColorNombre[]? colores = null, string[]? etiquetas = null) =>
        new(id, $"h{id}", $@"D:\Imagenes\{nombre}", new SizePx(ancho, alto), colores ?? [], favorita, etiquetas ?? [], DateTime.UtcNow);

    private static readonly IReadOnlyList<ImagenBiblioteca> Todas =
    [
        Img(1, "Amanecer10.jpg", 3840, 1080, colores: [ColorNombre.Naranja]),
        Img(2, "Amanecer2.jpg", 3840, 1080, favorita: true, colores: [ColorNombre.Azul]),
        Img(3, "Bosque.png", 1920, 1200, colores: [ColorNombre.Verde], etiquetas: ["Fantasía", "Noche"]),
        Img(4, "Retrato.jpg", 1080, 1920, favorita: true),
        Img(5, "Ciudad nocturna.jpg", 1920, 1080, colores: [ColorNombre.Azul, ColorNombre.Negro]),
    ];

    private static List<long> Ids(SeccionBiblioteca s, string texto = "", string? etiqueta = null, ColorNombre? color = null) =>
        FiltroBiblioteca.Aplicar(Todas, new CriterioBiblioteca(s, texto, etiqueta, color), Zonas, Lienzo).Select(i => i.Id).ToList();

    [Fact] public void Seccion_Panoramica() => Assert.Equal([2, 1], Ids(SeccionBiblioteca.Panoramica));
    [Fact] public void Seccion_UnMonitor() => Assert.Equal([3, 5], Ids(SeccionBiblioteca.UnMonitor));
    [Fact] public void Seccion_Otras() => Assert.Equal([4], Ids(SeccionBiblioteca.Otras));
    [Fact] public void Seccion_Favoritas() => Assert.Equal([2, 4], Ids(SeccionBiblioteca.Favoritas));
    [Fact] public void Seccion_Todas() => Assert.Equal(5, Ids(SeccionBiblioteca.Todas).Count);

    [Fact] public void Texto_SinAcentosNiMayusculas() => Assert.Equal([3], Ids(SeccionBiblioteca.Todas, "FANTASIA"));
    [Fact] public void Texto_VariasPalabras_TodasDebenCoincidir() => Assert.Equal([5], Ids(SeccionBiblioteca.Todas, "ciudad noct"));
    [Fact] public void Texto_EnEtiqueta() => Assert.Equal([3], Ids(SeccionBiblioteca.Todas, "noche"));
    [Fact] public void Texto_EnNombre() => Assert.Equal([2, 1], Ids(SeccionBiblioteca.Todas, "amanecer"));
    [Fact] public void Color_Filtra() => Assert.Equal([2, 5], Ids(SeccionBiblioteca.Todas, color: ColorNombre.Azul));
    [Fact] public void Etiqueta_Filtra() => Assert.Equal([3], Ids(SeccionBiblioteca.Todas, etiqueta: "noche"));
    [Fact] public void Orden_Natural() => Assert.Equal([2, 1], Ids(SeccionBiblioteca.Panoramica)); // Amanecer2 antes que Amanecer10
}
