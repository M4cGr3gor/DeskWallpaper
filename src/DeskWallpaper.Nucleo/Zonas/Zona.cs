namespace DeskWallpaper.Nucleo;

/// <summary>
/// Grupo de uno o más monitores que recibe una sola imagen.
/// </summary>
/// <param name="Id">Números de Windows de izquierda a derecha y de arriba abajo, unidos con "+" (p. ej. "2+1").</param>
/// <param name="MonitorIds">Ids de los monitores, en el mismo orden.</param>
public sealed record Zona(string Id, IReadOnlyList<string> MonitorIds)
{
    // Igualdad por contenido de la lista, no por referencia.
    public bool Equals(Zona? otra) =>
        otra is not null && Id == otra.Id && MonitorIds.SequenceEqual(otra.MonitorIds, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}
