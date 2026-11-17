using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using DeskWallpaper.Nucleo;
using Microsoft.Win32;

namespace DeskWallpaper.Windows;

/// <summary>
/// El ayudante en el arranque de Windows: una tarea programada «al iniciar sesión» del usuario, sin permisos de
/// administrador. Sustituye a la clave Run, que Windows retrasa (medido: el vídeo salía unos 40 s después de iniciar
/// sesión). Si no se puede crear la tarea, se vuelve a la clave Run. El nombre lleva el id de la carpeta de datos, para
/// que el ayudante pueda quitar la suya («Quitar fondo animado») sin tocar otra.
/// </summary>
public sealed partial class TareaArranque(string id) : IRegistroArranque
{
    private const string ClaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValorRun = "DeskWallpaper.Animado";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    internal static string Nombre(string id) => $"DeskWallpaper.Animado.{id}";

    public string? Leer()
    {
        var (codigo, salida) = Schtasks("/query", "/tn", Nombre(id), "/xml", "ONE");
        if (codigo == 0 && ComandoDe(salida) is { } comando) return comando;
        using var k = Registry.CurrentUser.OpenSubKey(ClaveRun);
        return k?.GetValue(ValorRun) as string;
    }

    public void Poner(string comando)
    {
        var (exe, argumentos) = Separar(comando);
        var xml = Path.Combine(Path.GetTempPath(), $"DeskWallpaper.Animado.{id}.xml");
        try
        {
            File.WriteAllText(xml, Xml(WindowsIdentity.GetCurrent().Name, exe, argumentos), Encoding.Unicode);
            var (codigo, _) = Schtasks("/create", "/tn", Nombre(id), "/xml", xml, "/f");
            if (codigo == 0)
            {
                QuitarRun();   // la de versiones anteriores: si no, arrancaría dos veces
                return;
            }
        }
        finally
        {
            try { File.Delete(xml); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        // Sin tarea (por ejemplo, el Programador de tareas desactivado por una directiva): la clave Run de siempre.
        using var k = Registry.CurrentUser.CreateSubKey(ClaveRun);
        k.SetValue(ValorRun, comando, RegistryValueKind.String);
    }

    public void Quitar()
    {
        Schtasks("/delete", "/tn", Nombre(id), "/f");
        QuitarRun();
    }

    /// <summary>Quita el valor de la clave Run, pero solo si es el de este ayudante (lleva su --id).</summary>
    private void QuitarRun()
    {
        using var k = Registry.CurrentUser.OpenSubKey(ClaveRun, writable: true);
        if (k?.GetValue(ValorRun) is string valor && valor.Contains($"--id {id}", StringComparison.OrdinalIgnoreCase))
            k.DeleteValue(ValorRun, throwOnMissingValue: false);
    }

    /// <summary>La tarea: al iniciar sesión el usuario, sin límite de tiempo, también con batería y con prioridad normal.</summary>
    internal static string Xml(string usuario, string exe, string argumentos)
    {
        var tarea = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Fondo animado de DeskWallpaper: lo pone al iniciar sesión.")),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", "true"), new XElement(Ns + "UserId", usuario))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", usuario),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "LeastPrivilege"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),   // por defecto Windows la pararía a los 3 días
                new XElement(Ns + "Priority", "5")),               // por defecto 7: por debajo de lo normal
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec", new XElement(Ns + "Command", exe), new XElement(Ns + "Arguments", argumentos))));
        return new XDocument(new XDeclaration("1.0", "UTF-16", null), tarea).Declaration + Environment.NewLine + tarea;
    }

    /// <summary>La orden de la tarea, en el mismo formato que <see cref="ServicioAnimado.ComandoArranque"/>; null si no la hay.</summary>
    internal static string? ComandoDe(string xml)
    {
        try
        {
            var exec = XDocument.Parse(xml).Descendants(Ns + "Exec").FirstOrDefault();
            if (exec?.Element(Ns + "Command")?.Value is not { Length: > 0 } exe) return null;
            var argumentos = exec.Element(Ns + "Arguments")?.Value ?? "";
            return argumentos.Length > 0 ? $"\"{exe}\" {argumentos}" : $"\"{exe}\"";
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Separa <c>"exe" argumentos</c> en sus dos partes.</summary>
    internal static (string Exe, string Argumentos) Separar(string comando)
    {
        if (comando.StartsWith('"') && comando.IndexOf('"', 1) is var fin and > 0)
            return (comando[1..fin], comando[(fin + 1)..].TrimStart());
        var espacio = comando.IndexOf(' ', StringComparison.Ordinal);
        return espacio < 0 ? (comando, "") : (comando[..espacio], comando[(espacio + 1)..]);
    }

    /// <summary>schtasks escribe en la página de códigos OEM de la consola (850 en español): con otra, los acentos se pierden.</summary>
    private static (int Codigo, string Salida) Schtasks(params string[] argumentos)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Oem(),
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");
            var salida = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15_000))
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
                return (-1, "");
            }
            return (p.ExitCode, salida.Result);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "");
        }
    }

    private static Encoding Oem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding((int)GetOEMCP()); }
        catch (ArgumentException) { return Encoding.Default; }
        catch (NotSupportedException) { return Encoding.Default; }
    }
}
