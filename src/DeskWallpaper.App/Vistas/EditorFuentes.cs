using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Lista editable de fuentes de búsqueda, dentro de Ajustes: activar, editar, añadir,
/// borrar, probar y restablecer las predeterminadas, sin recompilar el programa.
/// </summary>
public sealed class EditorFuentes : UserControl
{
    private readonly List<FuenteBusqueda> _fuentes;
    private readonly BuscadorOnline _buscador;
    private readonly StackPanel _lista = new() { Spacing = 6 };
    private readonly TextBlock _error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
    };

    /// <summary>Las fuentes tal como han quedado.</summary>
    public IReadOnlyList<FuenteBusqueda> Fuentes => _fuentes;

    public EditorFuentes(IReadOnlyList<FuenteBusqueda> fuentes, BuscadorOnline buscador)
    {
        _fuentes = [.. fuentes];
        _buscador = buscador;

        var anadirWeb = new Button { Content = "Añadir fuente web" };
        anadirWeb.Click += (_, _) => Anadir(new FuenteBusqueda("Nueva fuente web", TipoFuente.Web, true,
            "https://", "", "", new Dictionary<string, string>(), "", "", "", "", "", "", ""));
        var anadirApi = new Button { Content = "Añadir fuente con API" };
        anadirApi.Click += (_, _) => Anadir(new FuenteBusqueda("Nueva fuente con API", TipoFuente.Api, true,
            "https://", "", "", new Dictionary<string, string>(), "", "", "", "", "", "", ""));

        var restablecer = new Button { Content = "Restablecer predeterminadas" };
        var confirmar = new Button { Content = "Sí, restablecer", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var no = new Button { Content = "No" };
        var confirmacion = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Children = { new TextBlock { Text = "¿Sustituir la lista por las de serie (las de imágenes y las de vídeos)?", VerticalAlignment = VerticalAlignment.Center }, confirmar, no },
        };
        restablecer.Click += (_, _) => { confirmacion.Visibility = Visibility.Visible; restablecer.IsEnabled = false; };
        no.Click += (_, _) => { confirmacion.Visibility = Visibility.Collapsed; restablecer.IsEnabled = true; };
        confirmar.Click += (_, _) =>
        {
            _fuentes.Clear();
            _fuentes.AddRange(FuentesPredeterminadas.Todas());
            confirmacion.Visibility = Visibility.Collapsed;
            restablecer.IsEnabled = true;
            Pintar();
        };

        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                AyudaFuentes.Enlace("¿Qué es una fuente web y una fuente con API?", null),
                _error,
                _lista,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { anadirWeb, anadirApi, restablecer } },
                confirmacion,
            },
        };
        Pintar();
    }

    private void Anadir(FuenteBusqueda f)
    {
        _fuentes.Add(f);
        Pintar(abrir: _fuentes.Count - 1);
    }

    private void Pintar(int? abrir = null)
    {
        _lista.Children.Clear();
        if (_fuentes.Count == 0)
            _lista.Children.Add(new TextBlock { Text = "No hay fuentes: añade una o restablece las predeterminadas.", Foreground = Gris() });
        for (var i = 0; i < _fuentes.Count; i++) _lista.Children.Add(Fila(i, i == abrir));
    }

    /// <summary>Una fuente: casilla de activa y nombre en la cabecera; los campos al desplegarla.</summary>
    private Expander Fila(int indice, bool abierta)
    {
        var f = _fuentes[indice];
        void Cambiar(Func<FuenteBusqueda, FuenteBusqueda> cambio) => _fuentes[indice] = cambio(_fuentes[indice]);

        var activa = new CheckBox { IsChecked = f.Activa, Content = f.Nombre, MinWidth = 0 };
        activa.Checked += (_, _) => Cambiar(x => x with { Activa = true });
        activa.Unchecked += (_, _) => Cambiar(x => x with { Activa = false });
        var cabecera = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                activa,
                new TextBlock
                {
                    Text = (f.Tipo == TipoFuente.Api ? "API" : "Web") + (f.Contenido == ContenidoFuente.Videos ? " · fondos animados" : " · imágenes")
                           + (BuscadorOnline.FaltaClave(f) ? " · falta la clave" : ""),
                    Foreground = Gris(),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        var campos = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 4) };
        campos.Children.Add(AyudaFuentes.Enlace("¿Cómo se rellena cada opción?", f.Tipo));
        TextBox Campo(string titulo, string valor, Func<FuenteBusqueda, string, FuenteBusqueda> aplicar, string? ayuda = null, bool varias = false)
        {
            var caja = new TextBox
            {
                Header = titulo,
                Text = valor,
                PlaceholderText = ayuda ?? "",
                AcceptsReturn = varias,
                TextWrapping = varias ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = varias ? 64 : 0,
            };
            caja.TextChanged += (_, _) => Cambiar(x => aplicar(x, caja.Text));
            campos.Children.Add(caja);
            return caja;
        }

        var nombre = Campo("Nombre", f.Nombre, (x, v) => x with { Nombre = v });
        nombre.TextChanged += (_, _) => activa.Content = nombre.Text;
        var para = new ComboBox { Header = "Busca", Items = { "Imágenes", "Fondos animados (vídeos)" } };
        para.SelectedIndex = f.Contenido == ContenidoFuente.Videos ? 1 : 0;
        para.SelectionChanged += (_, _) =>
        {
            Cambiar(x => x with { Contenido = para.SelectedIndex == 1 ? ContenidoFuente.Videos : ContenidoFuente.Imagenes });
            DispatcherQueue.TryEnqueue(() => Pintar(indice));   // las rutas de los archivos de vídeo aparecen o se van
        };
        campos.Children.Add(para);
        var clave = new PasswordBox
        {
            Header = "Tu clave personal (si el servicio la pide; se pone donde diga {clave})",
            Password = f.Clave,
            PlaceholderText = "Se consigue gratis registrándose en la web del servicio",
        };
        clave.PasswordChanged += (_, _) => Cambiar(x => x with { Clave = clave.Password.Trim() });
        if (f.Tipo == TipoFuente.Api) campos.Children.Add(clave);
        campos.Children.Add(new TextBlock
        {
            Text = "Huecos: {texto} lo que buscas · {ancho} y {alto} la resolución · {proporcion} su forma (32x9) · {pagina} la página · {clave} tu clave.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Gris(),
        });
        if (f.Tipo == TipoFuente.Web)
        {
            Campo("Dirección con el tamaño", f.PlantillaExacta, (x, v) => x with { PlantillaExacta = v },
                  "https://www.ejemplo.com/buscar?q={texto}+{ancho}x{alto}");
            Campo("Dirección sin tamaño (opcional)", f.PlantillaSinResolucion, (x, v) => x with { PlantillaSinResolucion = v },
                  "Si se deja vacía, se quita de la anterior lo que lleva el tamaño");
        }
        else
        {
            Campo("Dirección: resolución exacta", f.PlantillaExacta, (x, v) => x with { PlantillaExacta = v });
            Campo("Dirección: mayor con la misma forma (opcional)", f.PlantillaMayor, (x, v) => x with { PlantillaMayor = v });
            Campo("Dirección sin tamaño (opcional)", f.PlantillaSinResolucion, (x, v) => x with { PlantillaSinResolucion = v });
            Campo("Cabeceras (una por línea, «Nombre: valor»), por ejemplo una clave", TextoCabeceras(f.Cabeceras),
                  (x, v) => x with { Cabeceras = LeerCabeceras(v) }, "X-API-Key: tu-clave", varias: true);
            Campo("Ruta de la lista de resultados en el JSON", f.RutaResultados, (x, v) => x with { RutaResultados = v.Trim() }, "data");
            Campo("Ruta de la miniatura", f.RutaMiniatura, (x, v) => x with { RutaMiniatura = v.Trim() }, "thumbs.large");
            Campo("Ruta de la imagen completa", f.RutaImagen, (x, v) => x with { RutaImagen = v.Trim() }, "path");
            Campo("Ruta del ancho", f.RutaAncho, (x, v) => x with { RutaAncho = v.Trim() }, "dimension_x");
            Campo("Ruta del alto", f.RutaAlto, (x, v) => x with { RutaAlto = v.Trim() }, "dimension_y");
            Campo("Ruta de la página de la imagen (opcional)", f.RutaFicha, (x, v) => x with { RutaFicha = v.Trim() }, "url");
            Campo("Ruta del número de la última página (opcional)", f.RutaUltimaPagina, (x, v) => x with { RutaUltimaPagina = v.Trim() }, "meta.last_page");
            if (f.Contenido == ContenidoFuente.Videos)
            {
                campos.Children.Add(new TextBlock
                {
                    Text = "Si cada vídeo trae varios archivos de distinto tamaño, se elige el más pequeño que cubre cada monitor. " +
                           "Entonces las rutas de imagen, ancho y alto de arriba no se usan.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = Gris(),
                });
                Campo("Ruta de la lista de archivos de cada vídeo (opcional)", f.RutaArchivos, (x, v) => x with { RutaArchivos = v.Trim() }, "video_files");
                Campo("Ruta de la dirección de cada archivo", f.RutaArchivoUrl, (x, v) => x with { RutaArchivoUrl = v.Trim() }, "link");
                Campo("Ruta del ancho de cada archivo", f.RutaArchivoAncho, (x, v) => x with { RutaArchivoAncho = v.Trim() }, "width");
                Campo("Ruta del alto de cada archivo", f.RutaArchivoAlto, (x, v) => x with { RutaArchivoAlto = v.Trim() }, "height");
                Campo("Ruta de la duración en segundos (opcional)", f.RutaDuracion, (x, v) => x with { RutaDuracion = v.Trim() }, "duration");
            }
        }

        var resultado = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true };
        var probar = new Button { Content = "Probar" };
        probar.Click += async (_, _) =>
        {
            probar.IsEnabled = false;
            resultado.Text = "Probando…";
            try { resultado.Text = await ProbarAsync(_fuentes[indice]); }
            catch (Exception ex) { resultado.Text = $"No se ha podido probar: {ex.Message}"; } // nunca cierra Ajustes
            finally { probar.IsEnabled = true; }
        };
        var borrar = new Button { Content = "Borrar" };
        borrar.Click += (_, _) =>
        {
            _fuentes.RemoveAt(indice);
            Pintar();
        };
        var botones = new Grid { ColumnSpacing = 8 };
        botones.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        botones.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        botones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(borrar, 1);
        Grid.SetColumn(resultado, 2);
        botones.Children.Add(probar);
        botones.Children.Add(borrar);
        botones.Children.Add(resultado);
        campos.Children.Add(botones);

        return new Expander
        {
            Header = cabecera,
            Content = campos,
            IsExpanded = abierta,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    /// <summary>
    /// Comprueba que todas tienen nombre y que no se repite (las páginas y los turnos van por nombre). Si hay un
    /// problema, lo muestra arriba de la lista y lo devuelve; null si todo está bien.
    /// </summary>
    public string? Validar()
    {
        string? error = null;
        if (_fuentes.Any(f => string.IsNullOrWhiteSpace(f.Nombre)))
            error = "Hay una fuente sin nombre: ponle uno antes de guardar.";
        else if (_fuentes.GroupBy(f => f.Nombre.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } repetido)
            error = $"Hay dos fuentes llamadas «{repetido.Key}»: cambia el nombre de una.";
        _error.Text = error ?? "";
        _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null) _error.StartBringIntoView();
        return error;
    }

    /// <summary>Api: búsquedas de ejemplo reales, sin y con tamaño. Web: solo comprueba que sale una dirección válida.</summary>
    private async Task<string> ProbarAsync(FuenteBusqueda f)
    {
        if (f.Tipo == TipoFuente.Web)
        {
            try
            {
                var uri = PlantillaUrl.Rellenar(f.PlantillaExacta, "mountain", new SizePx(3840, 1080), 1);
                return uri.Scheme is "http" or "https" ? $"Se abrirá: {uri.AbsoluteUri}" : "Plantilla no válida: tiene que empezar por https://";
            }
            catch (UriFormatException)
            {
                return "Plantilla no válida: no sale una dirección completa (https://…).";
            }
        }
        // Sin tamaño y con uno: así también se comprueban las direcciones de "exacta" y "mayor".
        var sin = await _buscador.BuscarAsync(f, "mountain", [], 1, CancellationToken.None);
        var con = await _buscador.BuscarAsync(f, "mountain", [new SizePx(3840, 1080)], 1, CancellationToken.None);
        static string Cuenta(ResultadoFuente r) =>
            r.Error ?? (r.Resultados.Count == 1 ? "1 resultado leído" : $"{r.Resultados.Count} resultados leídos");
        return $"Sin tamaño: {Cuenta(sin)}. Con 3840×1080: {Cuenta(con)}.";
    }

    private static string TextoCabeceras(IReadOnlyDictionary<string, string> c) =>
        string.Join("\r", c.Select(p => $"{p.Key}: {p.Value}"));

    private static Dictionary<string, string> LeerCabeceras(string texto)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var linea in texto.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dos = linea.IndexOf(':');
            if (dos > 0) d[linea[..dos].Trim()] = linea[(dos + 1)..].Trim();
        }
        return d;
    }

    private static Brush Gris() => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
}
