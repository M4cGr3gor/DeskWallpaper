using System.Xml.Linq;
using DeskWallpaper.Windows;

namespace DeskWallpaper.Pruebas;

/// <summary>
/// El arranque del ayudante con una tarea programada «al iniciar sesión» (Windows no la retrasa como a la clave Run).
/// Solo se prueba lo que no toca el sistema: el XML de la tarea y la orden que lleva.
/// </summary>
public class TareaArranquePruebas
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Valor(XDocument x, string elemento) => x.Descendants(Ns + elemento).Single().Value;

    [Fact]
    public void Xml_AlIniciarSesionDelUsuario_SinLimiteNiBateriaNiPrioridadBaja()
    {
        var x = XDocument.Parse(TareaArranque.Xml(@"PC\Usuario", @"C:\Prog\DeskWallpaper.Animado.exe", "--id 1"));

        Assert.Equal(@"PC\Usuario", x.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", Valor(x, "LogonType"));
        Assert.Equal("LeastPrivilege", Valor(x, "RunLevel"));
        Assert.Equal("PT0S", Valor(x, "ExecutionTimeLimit"));         // por defecto Windows la para a los 3 días
        Assert.Equal("false", Valor(x, "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Valor(x, "StopIfGoingOnBatteries"));
        Assert.Equal("IgnoreNew", Valor(x, "MultipleInstancesPolicy"));
        Assert.True(int.Parse(Valor(x, "Priority"), System.Globalization.CultureInfo.InvariantCulture) < 7); // 7 = por debajo de lo normal
    }

    [Fact]
    public void Comando_IdaYVuelta_ConEspaciosAcentosYAmpersand()
    {
        const string comando = "\"C:\\Prog & ñ\\DeskWallpaper.Animado.exe\" --datos \"C:\\Datos & á <x>\" --id 1a2b3c4d";

        var (exe, argumentos) = TareaArranque.Separar(comando);
        var xml = TareaArranque.Xml(@"PC\Usuario", exe, argumentos);

        Assert.Equal(@"C:\Prog & ñ\DeskWallpaper.Animado.exe", exe);
        Assert.Equal(comando, TareaArranque.ComandoDe(xml));
    }

    [Fact]
    public void ComandoDe_XmlRaroOVacio_DaNull()
    {
        Assert.Null(TareaArranque.ComandoDe(""));
        Assert.Null(TareaArranque.ComandoDe("<Task/>"));
    }

    [Fact]
    public void Nombre_LlevaElIdDeLaCarpetaDeDatos()
    {
        Assert.Equal("DeskWallpaper.Animado.1a2b3c4d", TareaArranque.Nombre("1a2b3c4d"));
    }
}
