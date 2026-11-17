using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>Coloca los hijos en filas y salta de línea cuando no caben (WinUI no trae WrapPanel).</summary>
public sealed class PanelFlujo : Panel
{
    public double Espacio { get; set; } = 6;

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size disponible)
    {
        double x = 0, y = 0, altoFila = 0, ancho = 0;
        foreach (var hijo in Children)
        {
            hijo.Measure(new global::Windows.Foundation.Size(disponible.Width, double.PositiveInfinity));
            var d = hijo.DesiredSize;
            if (x > 0 && x + d.Width > disponible.Width)
            {
                y += altoFila + Espacio;
                x = 0;
                altoFila = 0;
            }
            x += d.Width + Espacio;
            ancho = Math.Max(ancho, x - Espacio);
            altoFila = Math.Max(altoFila, d.Height);
        }
        return new global::Windows.Foundation.Size(ancho, Children.Count == 0 ? 0 : y + altoFila);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size final)
    {
        double x = 0, y = 0, altoFila = 0;
        foreach (var hijo in Children)
        {
            var d = hijo.DesiredSize;
            if (x > 0 && x + d.Width > final.Width)
            {
                y += altoFila + Espacio;
                x = 0;
                altoFila = 0;
            }
            hijo.Arrange(new global::Windows.Foundation.Rect(x, y, d.Width, d.Height));
            x += d.Width + Espacio;
            altoFila = Math.Max(altoFila, d.Height);
        }
        return final;
    }
}
