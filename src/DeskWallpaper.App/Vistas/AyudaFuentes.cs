using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Explicación de las opciones de una fuente de búsqueda, en un desplegable (que se sepa
/// cómo usar cada opción sin tener que adivinarlo).
/// </summary>
public static class AyudaFuentes
{
    /// <summary>Enlace que abre la ayuda de un tipo de fuente (o la general si es null).</summary>
    public static HyperlinkButton Enlace(string texto, TipoFuente? tipo)
    {
        var enlace = new HyperlinkButton { Content = texto, Padding = new Thickness(0, 2, 0, 2) };
        // El presentador del desplegable limita el ancho a unos 450 px y cortaba el texto: se amplía.
        var estilo = new Style(typeof(FlyoutPresenter));
        estilo.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 620.0));
        var ayuda = new Flyout
        {
            FlyoutPresenterStyle = estilo,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                MaxWidth = 560,
                Padding = new Thickness(0, 0, 12, 0),
                Content = tipo switch
                {
                    TipoFuente.Web => Web(),
                    TipoFuente.Api => Api(),
                    _ => General(),
                },
            },
        };
        // HyperlinkButton no tiene Flyout propio: se adjunta y se abre al pulsar.
        Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.SetAttachedFlyout(enlace, ayuda);
        enlace.Click += (s, _) => Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout((FrameworkElement)s);
        return enlace;
    }

    private static StackPanel General() => Bloque(
        ("Dos tipos de fuente", null),
        (null, "Web: una página de búsqueda (Google Imágenes, Bing…) que se abre tal cual en su propia pestaña, dentro del programa. Navegas como en el navegador y descargas con clic derecho › «Descargar con DeskWallpaper»."),
        (null, "API: un servicio que devuelve sus resultados en un formato que el programa sabe leer (JSON). Sus miniaturas se juntan en la pestaña «Resultados», marcadas como «exacta» o «mayor». Wallhaven es de este tipo."),
        (null, "Si un servicio cambia de dirección o de formato, se corrige aquí, sin reinstalar nada. «Probar» comprueba una fuente sin salir de Ajustes."),
        ("Huecos que se rellenan solos", null),
        (null, "{texto}: lo que escribes en la búsqueda.\n{ancho} y {alto}: la resolución de la casilla marcada (3840 y 1080).\n{proporcion}: su forma reducida (32x9, 16x10).\n{pagina}: el número de página (1, 2, 3…)."));

    private static StackPanel Web() => Bloque(
        ("Fuente web", null),
        (null, "Nombre: el título de su pestaña."),
        (null, "Dirección con el tamaño: la dirección de una búsqueda en esa web, con huecos donde van lo buscado y la resolución. Cómo obtenerla: busca en la web algo como «montaña 3840x1080», copia la dirección del navegador y cambia «montaña» por {texto} y «3840» y «1080» por {ancho} y {alto}."),
        (null, "Ejemplos:\nhttps://www.google.com/search?udm=2&q={texto}+imagesize:{ancho}x{alto}\nhttps://www.bing.com/images/search?q={texto}+{ancho}x{alto}"),
        (null, "Dirección sin tamaño (opcional): la que se usa al elegir «Sin filtro de tamaño». Si se deja vacía, se quita de la anterior lo que lleva el tamaño."),
        (null, "Probar: muestra la dirección que se abriría con «mountain» y 3840×1080."));

    private static StackPanel Api() => Bloque(
        ("Fuente con API", null),
        (null, "Nombre: el que sale en el pie de cada miniatura."),
        (null, "Dirección: resolución exacta: la búsqueda que devuelve imágenes de ese tamaño justo. Wallhaven: …&resolutions={ancho}x{alto}"),
        (null, "Dirección: mayor con la misma forma (opcional): la que devuelve ese tamaño o mayores con la misma forma; si existe, es la que se usa. Wallhaven: …&atleast={ancho}x{alto}&ratios={proporcion}"),
        (null, "Dirección sin tamaño (opcional): para buscar con todas las casillas desmarcadas. Vacía = la exacta sin lo que lleva el tamaño."),
        (null, "Cabeceras (opcional): datos que algunos servicios piden en cada petición, como una clave gratuita. Una por línea, «Nombre: valor». Ejemplo: X-API-Key: tu-clave"),
        ("Rutas en la respuesta", null),
        (null, "Dicen dónde está cada dato en la respuesta del servicio. Los niveles se separan con un punto. Con esta respuesta de Wallhaven:\n{ \"data\": [ { \"path\": \"https://…/imagen.jpg\", \"thumbs\": { \"large\": \"https://…/mini.jpg\" }, \"dimension_x\": 3840, \"dimension_y\": 1080, \"url\": \"https://wallhaven.cc/w/…\" } ], \"meta\": { \"last_page\": 3 } }"),
        (null, "Lista de resultados: data\nMiniatura: thumbs.large\nImagen completa: path\nAncho: dimension_x · Alto: dimension_y\nPágina de la imagen (opcional): url\nÚltima página (opcional): meta.last_page"),
        (null, "Probar: hace una búsqueda de ejemplo, con y sin tamaño, y dice cuántos resultados ha leído o qué ha fallado."));

    /// <summary>Títulos en negrita y párrafos que se ajustan al ancho.</summary>
    private static StackPanel Bloque(params (string? Titulo, string? Texto)[] partes)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 520 };
        foreach (var (titulo, texto) in partes)
        {
            if (titulo is not null)
                panel.Children.Add(new TextBlock { Text = titulo, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 4, 0, 0) });
            if (texto is not null)
            {
                var parrafo = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                var p = new Paragraph();
                p.Inlines.Add(new Run { Text = texto });
                parrafo.Blocks.Add(p);
                panel.Children.Add(parrafo);
            }
        }
        return panel;
    }
}
