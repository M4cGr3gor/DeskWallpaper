using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DeskWallpaper.Nucleo;

/// <summary>Una combinación aplicada, con su miniatura compuesta y cuándo se usó.</summary>
public sealed record EntradaHistorial(long Id, Combinacion Combinacion, byte[] Miniatura, DateTime Creada, DateTime UltimoUso, int Usos);

/// <summary>
/// Historial de combinaciones en <c>biblioteca.db</c>. La miniatura va dentro de la base
/// de datos y el número de entradas está acotado, así que no crea caché que pueda crecer sin control.
/// </summary>
public sealed class Historial(string carpetaDatos, int maximo = Historial.Maximo)
{
    public const int Maximo = 300;

    private string Cadena => new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(carpetaDatos, "biblioteca.db"),
        Pooling = false, // sin conexiones abiertas al terminar: el archivo se puede mover o borrar
    }.ToString();

    /// <summary>Registra un uso: si la combinación ya existe, actualiza su último uso; si no, la crea.</summary>
    public EntradaHistorial Registrar(Combinacion c, byte[] miniatura, DateTime ahora)
    {
        using var con = Abrir();
        using var tx = con.BeginTransaction();
        var clave = Clave(c);
        // En UTC: con la hora local, el cambio de horario alteraría el orden y el tope.
        var ahoraTexto = ahora.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO historial (clave, combinacion, miniatura, creada, ultimo_uso, usos)
                VALUES ($clave, $comb, $mini, $ahora, $ahora, 1)
                ON CONFLICT(clave) DO UPDATE SET ultimo_uso = $ahora, usos = usos + 1, miniatura = $mini;
                """;
            cmd.Parameters.AddWithValue("$clave", clave);
            cmd.Parameters.AddWithValue("$comb", JsonSerializer.Serialize(c));
            cmd.Parameters.AddWithValue("$mini", miniatura);
            cmd.Parameters.AddWithValue("$ahora", ahoraTexto);
            cmd.ExecuteNonQuery();
        }

        using (var recorte = con.CreateCommand())
        {
            recorte.Transaction = tx;
            recorte.CommandText = """
                DELETE FROM historial WHERE id NOT IN
                  (SELECT id FROM historial ORDER BY ultimo_uso DESC, id DESC LIMIT $max);
                """;
            recorte.Parameters.AddWithValue("$max", maximo);
            recorte.ExecuteNonQuery();
        }
        tx.Commit();

        return Leer(con, "WHERE clave = $clave", ("$clave", clave)).Single();
    }

    /// <summary>Entradas, la de uso más reciente primero.</summary>
    public IReadOnlyList<EntradaHistorial> Listar()
    {
        using var con = Abrir();
        return Leer(con, "ORDER BY ultimo_uso DESC, id DESC");
    }

    public void Borrar(long id)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM historial WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Rutas de imagen de la combinación que ya no existen en disco.</summary>
    public static IReadOnlyList<string> ImagenesQueFaltan(Combinacion c) =>
        c.Zonas.Select(z => z.RutaImagen).Where(r => !File.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Identifica una combinación: firma, monitores, rutas (sin distinguir mayúsculas) y encuadre.
    /// El orden de las zonas no importa.
    /// </summary>
    private static string Clave(Combinacion c)
    {
        var zonas = c.Zonas
            .Select(z => string.Join(",", z.MonitorIds.Select(id => id.ToUpperInvariant()))
                         + "|" + z.RutaImagen.ToUpperInvariant()
                         + "|" + string.Create(CultureInfo.InvariantCulture, $"{z.Encuadre.CentroX:F6};{z.Encuadre.CentroY:F6};{z.Encuadre.Zoom:F6}"))
            .OrderBy(s => s, StringComparer.Ordinal);
        var texto = c.FirmaDisposicion + "\n" + string.Join("\n", zonas);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
    }

    private SqliteConnection Abrir()
    {
        Directory.CreateDirectory(carpetaDatos);
        var con = new SqliteConnection(Cadena);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS historial (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              clave TEXT NOT NULL UNIQUE,
              combinacion TEXT NOT NULL,
              miniatura BLOB NOT NULL,
              creada TEXT NOT NULL,
              ultimo_uso TEXT NOT NULL,
              usos INTEGER NOT NULL);
            """;
        cmd.ExecuteNonQuery();
        return con;
    }

    private static List<EntradaHistorial> Leer(SqliteConnection con, string resto, params (string Nombre, object Valor)[] parametros)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT id, combinacion, miniatura, creada, ultimo_uso, usos FROM historial " + resto;
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        var lista = new List<EntradaHistorial>();
        while (r.Read())
        {
            var comb = JsonSerializer.Deserialize<Combinacion>(r.GetString(1));
            if (comb is null) continue;
            lista.Add(new EntradaHistorial(r.GetInt64(0), comb, (byte[])r[2],
                Fecha(r.GetString(3)), Fecha(r.GetString(4)), r.GetInt32(5)));
        }
        return lista;
    }

    /// <summary>Lee una fecha guardada (UTC, o local en bases antiguas) y la devuelve en hora local.</summary>
    private static DateTime Fecha(string texto) =>
        DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
}
