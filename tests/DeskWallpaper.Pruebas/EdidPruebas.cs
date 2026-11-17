using DeskWallpaper.Nucleo;
using DeskWallpaper.Windows;

namespace DeskWallpaper.Pruebas;

public class EdidPruebas
{
    private static byte[] EdidBase()
    {
        var b = new byte[128];
        byte[] cabecera = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        cabecera.CopyTo(b, 0);
        return b;
    }

    [Fact]
    public void Edid_TamanoDesdeDescriptor()
    {
        var b = EdidBase();
        b[21] = 60; b[22] = 34;
        b[66] = 0x55; b[67] = 0x50; b[68] = 0x21;
        Assert.Equal(new SizeMm(597, 336), Edid.TamanoFisico(b));
    }

    [Fact]
    public void Edid_SinDescriptor_UsaCm()
    {
        var b = EdidBase();
        b[21] = 35; b[22] = 21;
        Assert.Equal(new SizeMm(350, 210), Edid.TamanoFisico(b));
    }

    [Fact]
    public void Edid_SinNingunTamano_Null()
    {
        Assert.Null(Edid.TamanoFisico(EdidBase()));
    }

    [Fact]
    public void Edid_CabeceraInvalida_Null()
    {
        var b = EdidBase();
        b[0] = 0x12;
        b[21] = 35; b[22] = 21;
        Assert.Null(Edid.TamanoFisico(b));
    }

    [Fact]
    public void Edid_Corto_Null()
    {
        Assert.Null(Edid.TamanoFisico(new byte[10]));
    }

    [Fact]
    public void ClaveRegistro_Convierte()
    {
        Assert.Equal(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\GSM5BB8\5&10ce201e&0&UID4355\Device Parameters",
            Edid.ClaveRegistro(@"\\?\DISPLAY#GSM5BB8#5&10ce201e&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));
    }

    [Fact]
    public void ClaveRegistro_RutaRara_Null()
    {
        Assert.Null(Edid.ClaveRegistro(@"C:\no\es\un\monitor"));
        Assert.Null(Edid.ClaveRegistro(""));
    }
}
