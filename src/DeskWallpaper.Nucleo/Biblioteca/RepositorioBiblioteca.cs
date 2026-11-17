using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DeskWallpaper.Nucleo;

/// <summary>
/// Datos de la biblioteca en <c>biblioteca.db</c> (el mismo archivo que el historial). Una imagen
/// puede tener varias rutas (copias idénticas); si se queda sin ninguna, queda "ausente" y oculta,
/// pero conserva etiquetas y favorita por si vuelve (renombrada, movida o con el disco conectado).
/// </summary>
public sealed class RepositorioBiblioteca(string carpetaDatos)
{
    private string Cadena => new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(carpetaDatos, "biblioteca.db"),
        Pooling = false,
    }.ToString();

    /// <summary>
    /// Alta o actualización por huella, añadiendo la ruta a las conocidas de esa imagen. Si el archivo
    /// de una ruta conocida cambia de contenido (girado, retocado), conserva etiquetas y favorita: la
    /// imagen cambia de huella si esa era su única ruta, o la nueva las hereda si era una copia.
    /// </summary>
    public ImagenBiblioteca Guardar(string huella, string ruta, SizePx tamano, IReadOnlyList<ColorNombre> colores, long bytes, DateTime modificado,
                                    long duracionMs = 0)
    {
        using var con = Abrir();
        using var tx = con.BeginTransaction();
        var anterior = Escalar(con, tx, "SELECT imagen_id FROM rutas WHERE ruta = $r", ("$r", ruta));
        var existente = Escalar(con, tx, "SELECT id FROM imagenes WHERE huella = $h", ("$h", huella));
        long? heredarDe = null;
        if (anterior is { } idAnterior && existente is null)
        {
            var otras = Escalar(con, tx, "SELECT COUNT(*) FROM rutas WHERE imagen_id = $id AND ruta <> $r", ("$id", idAnterior), ("$r", ruta));
            if (otras == 0)
                Ejecutar(con, tx, "UPDATE imagenes SET huella = $h WHERE id = $id", ("$h", huella), ("$id", idAnterior));
            else
                heredarDe = idAnterior;
        }
        Ejecutar(con, tx, """
            INSERT INTO imagenes (huella, ancho, alto, colores, favorita, anadida, duracion_ms)
            VALUES ($h, $an, $al, $c, 0, $ahora, $d)
            ON CONFLICT(huella) DO UPDATE SET ancho = $an, alto = $al, colores = $c, duracion_ms = $d;
            """, ("$h", huella), ("$an", tamano.Ancho), ("$al", tamano.Alto), ("$c", string.Join(',', colores)),
            ("$ahora", Fecha(DateTime.UtcNow)), ("$d", duracionMs));
        Ejecutar(con, tx, """
            INSERT INTO rutas (ruta, imagen_id, bytes, modificado)
            VALUES ($r, (SELECT id FROM imagenes WHERE huella = $h), $b, $m)
            ON CONFLICT(ruta) DO UPDATE SET imagen_id = excluded.imagen_id, bytes = $b, modificado = $m;
            """, ("$r", ruta), ("$h", huella), ("$b", bytes), ("$m", Fecha(modificado)));
        if (heredarDe is { } origen)
        {
            Ejecutar(con, tx, """
                UPDATE imagenes SET favorita = (SELECT favorita FROM imagenes WHERE id = $o) WHERE huella = $h;
                INSERT OR IGNORE INTO etiquetas (imagen_id, etiqueta)
                  SELECT (SELECT id FROM imagenes WHERE huella = $h), etiqueta FROM etiquetas WHERE imagen_id = $o;
                """, ("$o", origen), ("$h", huella));
        }
        tx.Commit();
        return Leer(con, "WHERE i.huella = $h", ("$h", huella)).Single();
    }

    public ImagenBiblioteca? PorId(long id)
    {
        using var con = Abrir();
        return Leer(con, "WHERE i.id = $id", ("$id", id)).FirstOrDefault();
    }

    public ImagenBiblioteca? PorRuta(string ruta)
    {
        using var con = Abrir();
        return Leer(con, "WHERE i.id = (SELECT imagen_id FROM rutas WHERE ruta = $r)", ("$r", ruta)).FirstOrDefault();
    }

    /// <summary>Tamaño en bytes y fecha guardados de una ruta, para no recalcular lo que no ha cambiado.</summary>
    public (long Bytes, DateTime Modificado)? FirmaDeRuta(string ruta)
    {
        using var con = Abrir();
        using var cmd = Comando(con, null, "SELECT bytes, modificado FROM rutas WHERE ruta = $r", ("$r", ruta));
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0), LeerFecha(r.GetString(1))) : null;
    }

    /// <summary>Duración guardada del vídeo de esa ruta (0 si es una imagen o no la tiene); null si la ruta no está.</summary>
    public long? DuracionMsDeRuta(string ruta)
    {
        using var con = Abrir();
        using var cmd = Comando(con, null,
            "SELECT i.duracion_ms FROM rutas r JOIN imagenes i ON i.id = r.imagen_id WHERE r.ruta = $r", ("$r", ruta));
        return cmd.ExecuteScalar() is long ms ? ms : null;
    }

    /// <summary>Rutas conocidas dentro de una carpeta (y sus subcarpetas).</summary>
    public IReadOnlyList<string> RutasDentroDe(string carpeta)
    {
        var prefijo = carpeta.TrimEnd('\\', '/') + "\\";
        using var con = Abrir();
        using var cmd = Comando(con, null, "SELECT ruta FROM rutas WHERE substr(ruta, 1, $n) = $p COLLATE NOCASE",
            ("$n", prefijo.Length), ("$p", prefijo));
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read()) lista.Add(r.GetString(0));
        return lista;
    }

    /// <summary>Todas las rutas conocidas.</summary>
    public IReadOnlyList<string> Rutas()
    {
        using var con = Abrir();
        using var cmd = Comando(con, null, "SELECT ruta FROM rutas");
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read()) lista.Add(r.GetString(0));
        return lista;
    }

    public void QuitarRuta(string ruta) => QuitarRutas([ruta]);

    /// <summary>Quita varias rutas en una sola transacción (al quitar una carpeta grande).</summary>
    public void QuitarRutas(IEnumerable<string> rutas)
    {
        using var con = Abrir();
        using var tx = con.BeginTransaction();
        using var cmd = Comando(con, tx, "DELETE FROM rutas WHERE ruta = $r", ("$r", ""));
        foreach (var r in rutas)
        {
            cmd.Parameters["$r"].Value = r;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Imágenes presentes (con al menos una ruta). Ruta = la primera que existe en disco.</summary>
    public IReadOnlyList<ImagenBiblioteca> Listar()
    {
        using var con = Abrir();
        return Leer(con, "WHERE EXISTS (SELECT 1 FROM rutas r WHERE r.imagen_id = i.id)");
    }

    public void PonerFavorita(long id, bool favorita)
    {
        using var con = Abrir();
        Ejecutar(con, null, "UPDATE imagenes SET favorita = $f WHERE id = $id", ("$f", favorita ? 1 : 0), ("$id", id));
    }

    /// <summary>Sustituye las etiquetas: recorta, quita vacías y duplicadas sin distinguir mayúsculas.</summary>
    public void PonerEtiquetas(long id, IReadOnlyList<string> etiquetas)
    {
        var limpias = etiquetas.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var con = Abrir();
        using var tx = con.BeginTransaction();
        Ejecutar(con, tx, "DELETE FROM etiquetas WHERE imagen_id = $id", ("$id", id));
        foreach (var e in limpias)
            Ejecutar(con, tx, "INSERT OR IGNORE INTO etiquetas (imagen_id, etiqueta) VALUES ($id, $e)", ("$id", id), ("$e", e));
        tx.Commit();
    }

    /// <summary>
    /// Etiquetas con su número de imágenes presentes, la más usada primero. Se agrupan en .NET sin
    /// distinguir mayúsculas: el NOCASE de SQLite solo pliega ASCII ("Ñoño" y "ñoño" serían dos).
    /// </summary>
    public IReadOnlyList<(string Etiqueta, int Usos)> Etiquetas()
    {
        using var con = Abrir();
        using var cmd = Comando(con, null, """
            SELECT e.etiqueta, e.imagen_id FROM etiquetas e
            WHERE EXISTS (SELECT 1 FROM rutas r WHERE r.imagen_id = e.imagen_id);
            """);
        using var r = cmd.ExecuteReader();
        var filas = new List<(string Etiqueta, long Imagen)>();
        while (r.Read()) filas.Add((r.GetString(0), r.GetInt64(1)));
        return filas.GroupBy(f => f.Etiqueta, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Etiqueta: g.Select(f => f.Etiqueta).Min(StringComparer.Ordinal)!, Usos: g.Select(f => f.Imagen).Distinct().Count()))
            .OrderByDescending(e => e.Usos).ThenBy(e => e.Etiqueta, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<ImagenBiblioteca> Leer(SqliteConnection con, string donde, params (string, object)[] parametros)
    {
        var imagenes = new List<(long Id, string Huella, SizePx Tamano, string Colores, bool Favorita, DateTime Anadida, long DuracionMs)>();
        using (var cmd = Comando(con, null, "SELECT i.id, i.huella, i.ancho, i.alto, i.colores, i.favorita, i.anadida, i.duracion_ms FROM imagenes i " + donde, parametros))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                imagenes.Add((r.GetInt64(0), r.GetString(1), new SizePx(r.GetInt32(2), r.GetInt32(3)), r.GetString(4), r.GetInt32(5) != 0,
                              LeerFecha(r.GetString(6)), r.GetInt64(7)));

        // Con pocas imágenes (p. ej. al guardar una durante el escaneo) solo se leen sus filas: leerlo
        // todo cada vez haría el escaneo cuadrático con miles de imágenes.
        var filtro = imagenes.Count <= 500
            ? $" WHERE imagen_id IN ({string.Join(',', imagenes.Select(i => i.Id.ToString(CultureInfo.InvariantCulture)))})"
            : "";
        if (imagenes.Count == 0) return [];
        var rutas = Agrupar(con, "SELECT imagen_id, ruta FROM rutas" + filtro + " ORDER BY rowid");
        var etiquetas = Agrupar(con, "SELECT imagen_id, etiqueta FROM etiquetas" + filtro + " ORDER BY rowid");
        return imagenes.Select(i =>
        {
            var suyas = rutas.GetValueOrDefault(i.Id) ?? [];
            var existente = suyas.FirstOrDefault(File.Exists);
            var ruta = existente ?? suyas.FirstOrDefault() ?? "";
            var colores = i.Colores.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => Enum.TryParse<ColorNombre>(c, out var v) ? v : (ColorNombre?)null).OfType<ColorNombre>().ToList();
            return new ImagenBiblioteca(i.Id, i.Huella, ruta, i.Tamano, colores, i.Favorita,
                                        etiquetas.GetValueOrDefault(i.Id) ?? [], i.Anadida)
                { Disponible = existente is not null, DuracionMs = i.DuracionMs };
        }).ToList();
    }

    private static Dictionary<long, List<string>> Agrupar(SqliteConnection con, string sql)
    {
        using var cmd = Comando(con, null, sql);
        using var r = cmd.ExecuteReader();
        var grupos = new Dictionary<long, List<string>>();
        while (r.Read())
        {
            if (!grupos.TryGetValue(r.GetInt64(0), out var lista)) grupos[r.GetInt64(0)] = lista = [];
            lista.Add(r.GetString(1));
        }
        return grupos;
    }

    /// <summary>
    /// Versión del esquema (PRAGMA user_version). 2: las huellas de los BMP incluyen un trozo
    /// intermedio, así que al subir de versión se fuerza volver a leer sus rutas. 3: duración de los
    /// vídeos.
    /// </summary>
    public const int VersionEsquema = 3;

    private readonly Lock _esquema = new();
    private bool _esquemaListo;

    private SqliteConnection Abrir()
    {
        Directory.CreateDirectory(carpetaDatos);
        var con = new SqliteConnection(Cadena);
        con.Open();
        // El esquema se crea o se pone al día una sola vez por instancia, no en cada conexión.
        lock (_esquema)
        {
            if (!_esquemaListo)
            {
                PrepararEsquema(con);
                _esquemaListo = true;
            }
        }
        return con;
    }

    private static void PrepararEsquema(SqliteConnection con)
    {
        Ejecutar(con, null, """
            CREATE TABLE IF NOT EXISTS imagenes (
              id INTEGER PRIMARY KEY AUTOINCREMENT, huella TEXT NOT NULL UNIQUE, ancho INTEGER NOT NULL, alto INTEGER NOT NULL,
              colores TEXT NOT NULL, favorita INTEGER NOT NULL, anadida TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS rutas (
              ruta TEXT PRIMARY KEY COLLATE NOCASE, imagen_id INTEGER NOT NULL, bytes INTEGER NOT NULL, modificado TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS etiquetas (
              imagen_id INTEGER NOT NULL, etiqueta TEXT NOT NULL COLLATE NOCASE, UNIQUE(imagen_id, etiqueta));
            CREATE INDEX IF NOT EXISTS rutas_imagen ON rutas(imagen_id);
            """);
        var version = Escalar(con, null, "PRAGMA user_version") ?? 0;
        if (version >= VersionEsquema) return;
        using var tx = con.BeginTransaction();
        if (version < 2)
            Ejecutar(con, tx, "UPDATE rutas SET bytes = -1 WHERE lower(ruta) LIKE '%.bmp'");
        // Solo si falta: una base que se volviera a abrir con la versión rebajada ya la tendría.
        if (version < 3 && Escalar(con, tx, "SELECT COUNT(*) FROM pragma_table_info('imagenes') WHERE name = 'duracion_ms'") == 0)
            Ejecutar(con, tx, "ALTER TABLE imagenes ADD COLUMN duracion_ms INTEGER NOT NULL DEFAULT 0");
        Ejecutar(con, tx, $"PRAGMA user_version = {VersionEsquema}");
        tx.Commit();
    }

    private static SqliteCommand Comando(SqliteConnection con, SqliteTransaction? tx, string sql, params (string Nombre, object Valor)[] parametros)
    {
        var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }

    private static long? Escalar(SqliteConnection con, SqliteTransaction? tx, string sql, params (string, object)[] parametros)
    {
        using var cmd = Comando(con, tx, sql, parametros);
        return cmd.ExecuteScalar() is long v ? v : null;
    }

    private static void Ejecutar(SqliteConnection con, SqliteTransaction? tx, string sql, params (string, object)[] parametros)
    {
        using var cmd = Comando(con, tx, sql, parametros);
        cmd.ExecuteNonQuery();
    }

    private static string Fecha(DateTime d) => d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime LeerFecha(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
