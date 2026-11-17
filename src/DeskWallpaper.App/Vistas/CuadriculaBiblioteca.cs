using System.Collections.ObjectModel;
using System.ComponentModel;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace DeskWallpaper.App.Vistas;

/// <summary>Una miniatura de la cuadrícula (se rellena al hacerse visible).</summary>
public sealed class ElementoMiniatura(ImagenBiblioteca imagen) : INotifyPropertyChanged
{
    private BitmapImage? _miniatura;

    public ImagenBiblioteca Imagen { get; set; } = imagen;
    public string Nombre => Imagen.Nombre;
    private bool _encima;

    /// <summary>Estrella rellena si es favorita; de contorno (para marcarla) si no.</summary>
    public string Estrella => Imagen.Favorita ? "\uE735" : "\uE734";

    /// <summary>La de contorno solo se ve con el ratón encima.</summary>
    public double OpacidadEstrella => Imagen.Favorita ? 1 : _encima ? 0.9 : 0;

    public bool Encima
    {
        get => _encima;
        set { _encima = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpacidadEstrella))); }
    }

    public BitmapImage? Miniatura
    {
        get => _miniatura;
        set { _miniatura = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Miniatura))); }
    }

    /// <summary>Las de un disco desconectado se ven atenuadas y con la marca "No disponible".</summary>
    public double OpacidadImagen => Imagen.Disponible ? 1 : 0.35;

    public Visibility VisibilidadNoDisponible => Imagen.Disponible ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Fondo animado: ▶ con su duración abajo a la derecha.</summary>
    public Visibility VisibilidadVideo => Imagen.EsVideo ? Visibility.Visible : Visibility.Collapsed;

    public string TextoDuracion => Imagen.EsVideo ? "▶ " + FormatosVideo.TextoDuracion(TimeSpan.FromMilliseconds(Imagen.DuracionMs)) : "";

    public void Avisar()
    {
        foreach (var p in new[] { nameof(Estrella), nameof(OpacidadEstrella), nameof(OpacidadImagen), nameof(VisibilidadNoDisponible) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Cuadrícula virtualizada de la biblioteca: miniaturas en segundo plano, favorita (estrella o tecla F),
/// menú contextual, arrastrar al lienzo y doble clic para verla a pantalla completa.
/// </summary>
public sealed class CuadriculaBiblioteca : UserControl
{
    /// <summary>Formato propio del arrastre: la ruta de la imagen.</summary>
    public const string FormatoRuta = "DeskWallpaper.Ruta";

    private readonly GridView _vista;
    private readonly ObservableCollection<ElementoMiniatura> _elementos = [];
    private readonly HashSet<string> _cargando = [];
    /// <summary>Miniaturas generándose a la vez (desplazarse deprisa no debe saturar CPU y memoria).</summary>
    private readonly SemaphoreSlim _limite = new(Math.Max(2, Environment.ProcessorCount / 2));
    private Func<ImagenBiblioteca, Task<string?>>? _obtenerMiniatura;
    private double _anchoElemento = 256, _altoElemento = 96;

    public event EventHandler<ImagenBiblioteca>? Ver;
    public event EventHandler<ImagenBiblioteca>? UsarEnZona;
    public event EventHandler<ImagenBiblioteca>? CambiarFavorita;
    public event EventHandler<ImagenBiblioteca>? Etiquetar;
    public event EventHandler<ImagenBiblioteca>? QuitarMarcas;
    public event EventHandler<ImagenBiblioteca>? AmpliarIA;
    public event EventHandler<ImagenBiblioteca>? QuitarRuido;

    public CuadriculaBiblioteca()
    {
        _vista = new GridView
        {
            ItemsSource = _elementos,
            SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true,
            IsItemClickEnabled = false,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Grid CornerRadius="4" Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}">
                    <Image Source="{Binding Miniatura}" Stretch="UniformToFill" HorizontalAlignment="Center" VerticalAlignment="Center"
                           Opacity="{Binding OpacidadImagen}" />
                    <Border Background="#B0000000" CornerRadius="3" Padding="6,2" Margin="4" HorizontalAlignment="Left" VerticalAlignment="Bottom"
                            Visibility="{Binding VisibilidadNoDisponible}">
                      <TextBlock Text="No disponible" FontSize="11" Foreground="White" />
                    </Border>
                    <Border Background="#B0000000" CornerRadius="3" Padding="6,2" Margin="4" HorizontalAlignment="Right" VerticalAlignment="Bottom"
                            Visibility="{Binding VisibilidadVideo}">
                      <TextBlock Text="{Binding TextoDuracion}" FontSize="11" Foreground="White" />
                    </Border>
                    <Border Tag="Estrella" Background="#66000000" CornerRadius="12" Width="28" Height="28" Margin="0,4,4,0"
                            HorizontalAlignment="Right" VerticalAlignment="Top" Opacity="{Binding OpacidadEstrella}"
                            ToolTipService.ToolTip="Favorita (F)">
                      <TextBlock Tag="Estrella" Text="{Binding Estrella}" FontFamily="Segoe Fluent Icons" FontSize="16" Foreground="Gold"
                                 HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                  </Grid>
                </DataTemplate>
                """),
        };
        _vista.ContainerContentChanging += AlHacerseVisible;
        _vista.DragItemsStarting += (_, e) =>
        {
            // Una imagen de un disco desconectado no se puede poner de fondo.
            if (e.Items.FirstOrDefault() is not ElementoMiniatura { Imagen.Disponible: true } el) { e.Cancel = true; return; }
            e.Data.SetData(FormatoRuta, el.Imagen.Ruta);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        };
        _vista.Tapped += (_, e) =>
        {
            if (EsEstrella(e.OriginalSource) && ElementoDe(e.OriginalSource) is { } el)
            {
                CambiarFavorita?.Invoke(this, el.Imagen);
                e.Handled = true;
            }
        };
        _vista.DoubleTapped += (_, e) =>
        {
            if (!EsEstrella(e.OriginalSource) && ElementoDe(e.OriginalSource) is { } el) Ver?.Invoke(this, el.Imagen);
        };
        _vista.KeyDown += (_, e) =>
        {
            if (_vista.SelectedItem is not ElementoMiniatura el) return;
            if (e.Key == global::Windows.System.VirtualKey.F) { CambiarFavorita?.Invoke(this, el.Imagen); e.Handled = true; }
            else if (e.Key == global::Windows.System.VirtualKey.Enter) { Ver?.Invoke(this, el.Imagen); e.Handled = true; }
        };
        Content = _vista;
    }

    public ImagenBiblioteca? Seleccionada => (_vista.SelectedItem as ElementoMiniatura)?.Imagen;

    /// <param name="obtenerMiniatura">Devuelve la ruta del JPEG de la miniatura (o null si no se puede).</param>
    public void Mostrar(IReadOnlyList<ImagenBiblioteca> imagenes, double proporcion, int tamano,
                        Func<ImagenBiblioteca, Task<string?>> obtenerMiniatura)
    {
        _obtenerMiniatura = obtenerMiniatura;
        // Las miniaturas ya cargadas se conservan (por huella) para que filtrar no parpadee.
        var cargadas = _elementos.Where(e => e.Miniatura is not null).ToDictionary(e => e.Imagen.Huella, e => e.Miniatura!);
        var seleccion = Seleccionada?.Huella;
        // Se reutiliza la colección: sustituir el ItemsSource recrea el panel y pierde el tamaño de los elementos.
        _elementos.Clear();
        foreach (var img in imagenes)
            _elementos.Add(new ElementoMiniatura(img) { Miniatura = cargadas.GetValueOrDefault(img.Huella) });
        CambiarTamano(proporcion, tamano);
        if (seleccion is not null) _vista.SelectedItem = _elementos.FirstOrDefault(e => e.Imagen.Huella == seleccion);
    }

    /// <summary>Actualiza una imagen (favorita, etiquetas) sin recargar la cuadrícula.</summary>
    public void Actualizar(ImagenBiblioteca imagen)
    {
        if (_elementos.FirstOrDefault(e => e.Imagen.Id == imagen.Id) is { } el)
        {
            el.Imagen = imagen;
            el.Avisar();
            if (_vista.ContainerFromItem(el) is GridViewItem contenedor) Decorar(contenedor, el);
        }
    }

    /// <summary>Tras vaciar la caché: olvida las miniaturas y vuelve a pedir las visibles.</summary>
    public void RecargarMiniaturas()
    {
        foreach (var el in _elementos)
        {
            el.Miniatura = null;
            if (_obtenerMiniatura is not null && _vista.ContainerFromItem(el) is not null && _cargando.Add(el.Imagen.Huella))
                _ = CargarAsync(el);
        }
    }

    /// <param name="proporcion">Ancho/alto de las miniaturas de la sección (panorámicas más anchas).</param>
    public void CambiarTamano(double proporcion, int tamano)
    {
        _altoElemento = tamano * 0.6;
        _anchoElemento = _altoElemento * proporcion;
        if (_vista.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemHeight = _altoElemento + 8;
            panel.ItemWidth = _anchoElemento + 8;
        }
    }

    private void AlHacerseVisible(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // Al principio ItemWidth es NaN (y NaN no es "distinto" en una comparación): se mira aparte.
        if (sender.ItemsPanelRoot is ItemsWrapGrid panel
            && (double.IsNaN(panel.ItemWidth) || Math.Abs(panel.ItemWidth - (_anchoElemento + 8)) > 0.5
                || Math.Abs(panel.ItemHeight - (_altoElemento + 8)) > 0.5))
        {
            panel.ItemHeight = _altoElemento + 8;
            panel.ItemWidth = _anchoElemento + 8;
        }
        if (args.InRecycleQueue || args.Item is not ElementoMiniatura el) return;

        var contenedor = (GridViewItem)args.ItemContainer;
        Decorar(contenedor, el);
        if (contenedor.Tag is null)
        {
            // Una sola vez por contenedor (se reciclan): la estrella de contorno aparece al pasar el ratón.
            contenedor.Tag = true;
            contenedor.PointerEntered += (s, _) => { if (((GridViewItem)s).Content is ElementoMiniatura e) e.Encima = true; };
            contenedor.PointerExited += (s, _) => { if (((GridViewItem)s).Content is ElementoMiniatura e) e.Encima = false; };
        }
        el.Encima = false;

        if (el.Miniatura is null && _obtenerMiniatura is not null && _cargando.Add(el.Imagen.Huella))
            _ = CargarAsync(el);
    }

    /// <summary>Tooltip y menú con el estado actual de la imagen (favorita, etiquetas).</summary>
    private void Decorar(GridViewItem contenedor, ElementoMiniatura el)
    {
        ToolTipService.SetToolTip(contenedor, $"{el.Nombre}\n{el.Imagen.Tamano.Ancho}×{el.Imagen.Tamano.Alto}"
                                              + (el.Imagen.Disponible ? "" : "\nNo disponible: su carpeta no está conectada")
                                              + (el.Imagen.Etiquetas.Count > 0 ? "\n" + string.Join(", ", el.Imagen.Etiquetas) : ""));
        contenedor.ContextFlyout = Menu(el);
    }

    private async Task CargarAsync(ElementoMiniatura el)
    {
        var huella = el.Imagen.Huella;
        await _limite.WaitAsync();
        try
        {
            var ruta = await _obtenerMiniatura!(el.Imagen);
            if (ruta is null) return;
            var miniatura = new BitmapImage(new Uri(ruta));
            el.Miniatura = miniatura;
            // Si mientras tanto se ha vuelto a filtrar, la cuadrícula tiene otro elemento para esta imagen.
            foreach (var actual in _elementos.Where(e => e.Miniatura is null && e.Imagen.Huella == huella))
                actual.Miniatura = miniatura;
        }
        finally
        {
            _limite.Release();
            _cargando.Remove(huella);
        }
    }

    private MenuFlyout Menu(ElementoMiniatura el)
    {
        MenuFlyoutItem Opcion(string texto, string glifo, Action accion)
        {
            var o = new MenuFlyoutItem { Text = texto, Icon = new FontIcon { Glyph = glifo } };
            o.Click += (_, _) => accion();
            return o;
        }
        var menu = new MenuFlyout
        {
            Items =
            {
                Opcion("Ver a pantalla completa", "", () => Ver?.Invoke(this, el.Imagen)),
                Opcion("Usar en la zona seleccionada", "", () => UsarEnZona?.Invoke(this, el.Imagen)),
                Opcion("Quitar marcas u objetos…", "", () => QuitarMarcas?.Invoke(this, el.Imagen)),
                Opcion("Ampliar con IA…", "", () => AmpliarIA?.Invoke(this, el.Imagen)),
                Opcion("Quitar artefactos y ruido…", "", () => QuitarRuido?.Invoke(this, el.Imagen)),
                new MenuFlyoutSeparator(),
                Opcion(el.Imagen.Favorita ? "Quitar de favoritas" : "Marcar como favorita", el.Imagen.Favorita ? "" : "",
                       () => CambiarFavorita?.Invoke(this, el.Imagen)),
                Opcion("Etiquetar…", "", () => Etiquetar?.Invoke(this, el.Imagen)),
                new MenuFlyoutSeparator(),
                Opcion("Abrir ubicación", "", () =>
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{el.Imagen.Ruta}\"")?.Dispose()),
                Opcion("Copiar ruta", "", () =>
                {
                    var paquete = new DataPackage();
                    paquete.SetText(el.Imagen.Ruta);
                    Clipboard.SetContent(paquete);
                }),
            },
        };
        // Las herramientas de IA son solo para imágenes.
        if (el.Imagen.EsVideo)
            foreach (var ia in menu.Items.OfType<MenuFlyoutItem>()
                         .Where(i => i.Text is "Quitar marcas u objetos…" or "Ampliar con IA…" or "Quitar artefactos y ruido…").ToList())
                menu.Items.Remove(ia);
        return menu;
    }

    private static bool EsEstrella(object origen) => origen is FrameworkElement { Tag: "Estrella" };

    private static ElementoMiniatura? ElementoDe(object origen) =>
        (origen as FrameworkElement)?.DataContext as ElementoMiniatura;
}
