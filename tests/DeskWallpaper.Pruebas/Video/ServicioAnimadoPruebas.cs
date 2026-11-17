using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Video;

internal sealed class ControlFalso : IControlAyudante
{
    public bool Encendido { get; set; }
    public int Arranques, Recargas, Cierres;
    public bool EnMarcha() => Encendido;
    public void Arrancar() { Arranques++; Encendido = true; }
    public void Recargar() => Recargas++;
    public void Cerrar() { Cierres++; Encendido = false; }
}

internal sealed class RegistroFalso : IRegistroArranque
{
    public string? Valor { get; set; }
    public int Escrituras;
    public string? Leer() => Valor;
    public void Poner(string comando) { Escrituras++; Valor = comando; }
    public void Quitar() { Escrituras++; Valor = null; }
}

/// <summary>Arranque con Windows y control del ayudante.</summary>
public class ServicioAnimadoPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ControlFalso _control = new();
    private readonly RegistroFalso _registro = new();
    private readonly string _datos;
    private const string Ayudante = @"C:\Programas\DeskWallpaper\DeskWallpaper.Animado.exe";

    public ServicioAnimadoPruebas() => _datos = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "datos")).FullName;

    public void Dispose() => _img.Dispose();

    private ServicioAnimado Servicio(bool porDefecto = true) => new(_datos, porDefecto, Ayudante, _control, _registro);

    private static ConfigAnimado ConVideo => new(30, true, false, [new ZonaAnimada(@"C:\v\a.mp4", [new PantallaAnimada(M1.Id, 0, 0, 1, 1)])]);
    private static ConfigAnimado SinZonas => new(30, true, false, []);

    [Fact]
    public void Activar_ConVideo_GuardaPoneArranqueYArranca()
    {
        var s = Servicio();

        s.Activar(ConVideo);

        Assert.Equal(ConVideo.Zonas[0].Video, AlmacenAnimado.Leer(_datos)!.Zonas[0].Video);
        Assert.Equal(s.ComandoArranque, _registro.Valor);
        Assert.Equal($"\"{Ayudante}\" --datos \"{_datos}\" --id {ServicioAnimado.Id(_datos)}", s.ComandoArranque);
        Assert.Equal(1, _control.Arranques);
        Assert.Equal(0, _control.Recargas);
    }

    [Fact]
    public void Activar_ConVideoYaEnMarcha_Recarga()
    {
        _control.Encendido = true;

        Servicio().Activar(ConVideo);

        Assert.Equal(1, _control.Recargas);
        Assert.Equal(0, _control.Arranques);
    }

    [Fact]
    public void Activar_SinZonas_CierraQuitaArranqueYDejaJsonVacio()
    {
        _control.Encendido = true;
        _registro.Valor = "algo";

        Servicio().Activar(SinZonas);

        Assert.Equal(1, _control.Cierres);
        Assert.Null(_registro.Valor);
        Assert.Empty(AlmacenAnimado.Leer(_datos)!.Zonas);
    }

    [Fact]
    public void Activar_CarpetaNoPorDefecto_NuncaTocaElArranque()
    {
        var s = Servicio(porDefecto: false);

        s.Activar(ConVideo);
        s.Activar(SinZonas);
        s.CorregirArranque();

        Assert.Equal(0, _registro.Escrituras);
        Assert.Equal(1, _control.Arranques);
        Assert.Equal(1, _control.Cierres);
    }

    [Fact]
    public void Restaurar_VuelveAlJsonYEstadoAnteriores()
    {
        var s = Servicio();
        var anterior = s.Capturar();   // sin archivo y parado
        s.Activar(ConVideo);

        s.Restaurar(anterior);

        Assert.Null(anterior.Config);
        Assert.False(File.Exists(Path.Combine(_datos, AlmacenAnimado.Archivo)));
        Assert.Equal(1, _control.Cierres);
        Assert.Null(_registro.Valor);
    }

    [Fact]
    public void Restaurar_AyudanteQueEstabaEnMarcha_VuelveAEstarlo()
    {
        var s = Servicio();
        s.Activar(ConVideo);
        var anterior = s.Capturar();
        s.Activar(SinZonas);

        s.Restaurar(anterior);

        Assert.True(_control.Encendido);
        Assert.Single(AlmacenAnimado.Leer(_datos)!.Zonas);
        Assert.Equal(s.ComandoArranque, _registro.Valor);
    }

    [Fact]
    public void CorregirArranque_RutaDistinta_LaPone()
    {
        AlmacenAnimado.Guardar(_datos, ConVideo);
        _registro.Valor = "\"D:\\Viejo\\DeskWallpaper.Animado.exe\" --datos x --id y";
        var s = Servicio();

        s.CorregirArranque();

        Assert.Equal(s.ComandoArranque, _registro.Valor);
    }

    [Fact]
    public void CorregirArranque_SinZonas_NoPoneNada()
    {
        AlmacenAnimado.Guardar(_datos, SinZonas);

        Servicio().CorregirArranque();

        Assert.Null(_registro.Valor);
        Assert.Equal(0, _registro.Escrituras);
    }

    [Fact]
    public void Id_IgualConBarraFinalYMayusculas()
    {
        Assert.Equal(ServicioAnimado.Id(@"C:\Datos\Prueba"), ServicioAnimado.Id(@"c:\datos\PRUEBA\"));
        Assert.Matches("^[0-9a-f]{8}$", ServicioAnimado.Id(@"C:\Datos\Prueba"));
        Assert.NotEqual(ServicioAnimado.Id(@"C:\Datos\Prueba"), ServicioAnimado.Id(@"C:\Datos\Otra"));
    }
}
