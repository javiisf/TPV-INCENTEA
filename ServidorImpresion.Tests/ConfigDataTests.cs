using System;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class ConfigDataTests
{
    // ── Sanitizar: puerto ─────────────────────────────────────────────────────

    [Fact]
    public void Sanitizar_PortZero_ResetsToDefault()
    {
        var c = new ConfigData { PuertoServidor = 0 };
        c.Sanitizar();
        Assert.Equal(8080, c.PuertoServidor);
    }

    [Fact]
    public void Sanitizar_PortNegative_ResetsToDefault()
    {
        var c = new ConfigData { PuertoServidor = -1 };
        c.Sanitizar();
        Assert.Equal(8080, c.PuertoServidor);
    }

    [Fact]
    public void Sanitizar_PortTooHigh_ResetsToDefault()
    {
        var c = new ConfigData { PuertoServidor = 70000 };
        c.Sanitizar();
        Assert.Equal(8080, c.PuertoServidor);
    }

    [Fact]
    public void Sanitizar_ValidPort_Unchanged()
    {
        var c = new ConfigData { PuertoServidor = 9000 };
        c.Sanitizar();
        Assert.Equal(9000, c.PuertoServidor);
    }

    // ── Sanitizar: baudios ────────────────────────────────────────────────────

    [Fact]
    public void Sanitizar_BaudRateTooLow_ResetsToDefault()
    {
        var c = new ConfigData { BaudRate = 100 };
        c.Sanitizar();
        Assert.Equal(9600, c.BaudRate);
    }

    [Fact]
    public void Sanitizar_BaudRateTooHigh_ResetsToDefault()
    {
        var c = new ConfigData { BaudRate = 200000 };
        c.Sanitizar();
        Assert.Equal(9600, c.BaudRate);
    }

    [Fact]
    public void Sanitizar_ValidBaudRate_Unchanged()
    {
        var c = new ConfigData { BaudRate = 115200 };
        c.Sanitizar();
        Assert.Equal(115200, c.BaudRate);
    }

    // ── Sanitizar: tamaño de ticket ───────────────────────────────────────────

    [Fact]
    public void Sanitizar_MaxTicketBytesTooSmall_ResetsToDefault()
    {
        var c = new ConfigData { MaxTicketBytes = 500 }; // < 1024
        c.Sanitizar();
        Assert.Equal(512 * 1024, c.MaxTicketBytes);
    }

    [Fact]
    public void Sanitizar_MaxTicketBytesTooLarge_ResetsToDefault()
    {
        var c = new ConfigData { MaxTicketBytes = 20 * 1024 * 1024 }; // > 10 MB
        c.Sanitizar();
        Assert.Equal(512 * 1024, c.MaxTicketBytes);
    }

    [Fact]
    public void Sanitizar_ValidMaxTicketBytes_Unchanged()
    {
        var c = new ConfigData { MaxTicketBytes = 1024 };
        c.Sanitizar();
        Assert.Equal(1024, c.MaxTicketBytes);
    }

    // ── Sanitizar: acumulados ─────────────────────────────────────────────────

    [Fact]
    public void Sanitizar_NegativeTrabajosAcumulados_ResetsToZero()
    {
        var c = new ConfigData { TrabajosAcumulados = -10 };
        c.Sanitizar();
        Assert.Equal(0, c.TrabajosAcumulados);
    }

    [Fact]
    public void Sanitizar_NegativeFallosAcumulados_ResetsToZero()
    {
        var c = new ConfigData { FallosAcumulados = -3 };
        c.Sanitizar();
        Assert.Equal(0, c.FallosAcumulados);
    }

    [Fact]
    public void Sanitizar_PositiveAccumulados_Unchanged()
    {
        var c = new ConfigData { TrabajosAcumulados = 100, FallosAcumulados = 5 };
        c.Sanitizar();
        Assert.Equal(100, c.TrabajosAcumulados);
        Assert.Equal(5, c.FallosAcumulados);
    }

    // ── EsValida ──────────────────────────────────────────────────────────────

    [Fact]
    public void EsValida_NoDevice_ReturnsFalse()
    {
        var c = new ConfigData { PuertoServidor = 8080 };
        Assert.False(c.EsValida());
    }

    [Fact]
    public void EsValida_WithCOM_ReturnsTrue()
    {
        var c = new ConfigData { PuertoServidor = 8080, UltimoCOM = "COM3" };
        Assert.True(c.EsValida());
    }

    [Fact]
    public void EsValida_WithUSB_ReturnsTrue()
    {
        var c = new ConfigData { PuertoServidor = 8080, UltimaUSB = "USB001" };
        Assert.True(c.EsValida());
    }

    [Fact]
    public void EsValida_InvalidPort_ReturnsFalse()
    {
        var c = new ConfigData { PuertoServidor = 0, UltimoCOM = "COM1" };
        Assert.False(c.EsValida());
    }

    [Fact]
    public void EsValida_WhitespaceDevices_ReturnsFalse()
    {
        var c = new ConfigData { PuertoServidor = 8080, UltimoCOM = "   ", UltimaUSB = "  " };
        Assert.False(c.EsValida());
    }

    // ── AplicarDispositivo ────────────────────────────────────────────────────

    [Fact]
    public void AplicarDispositivo_COM_SetsCOMAndClearsUSB()
    {
        var c = new ConfigData { UltimaUSB = "USB001" };
        c.AplicarDispositivo("COM3");
        Assert.Equal("COM3", c.UltimoCOM);
        Assert.Equal("", c.UltimaUSB);
    }

    [Fact]
    public void AplicarDispositivo_USB_SetsUSBAndClearsCOM()
    {
        var c = new ConfigData { UltimoCOM = "COM3" };
        c.AplicarDispositivo("USB001");
        Assert.Equal("USB001", c.UltimaUSB);
        Assert.Equal("", c.UltimoCOM);
    }

    [Fact]
    public void AplicarDispositivo_COMCaseInsensitive_SetsCOM()
    {
        var c = new ConfigData();
        c.AplicarDispositivo("com1");
        Assert.Equal("com1", c.UltimoCOM);
        Assert.Equal("", c.UltimaUSB);
    }

    [Fact]
    public void AplicarDispositivo_EmptyString_Throws()
    {
        var c = new ConfigData();
        Assert.Throws<ArgumentException>(() => c.AplicarDispositivo(""));
    }

    // ── Clone ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Clone_ReturnsNewInstance()
    {
        var original = new ConfigData { PuertoServidor = 9000 };
        var clone = original.Clone();
        Assert.NotSame(original, clone);
    }

    [Fact]
    public void Clone_PreservesValues()
    {
        var original = new ConfigData
        {
            PuertoServidor = 9090,
            UltimoCOM = "COM3",
            ApiKey = "mi-clave",
            BaudRate = 115200,
            TrabajosAcumulados = 42
        };
        var clone = original.Clone();
        Assert.Equal(9090, clone.PuertoServidor);
        Assert.Equal("COM3", clone.UltimoCOM);
        Assert.Equal("mi-clave", clone.ApiKey);
        Assert.Equal(115200, clone.BaudRate);
        Assert.Equal(42, clone.TrabajosAcumulados);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        var original = new ConfigData { PuertoServidor = 9000 };
        var clone = original.Clone();
        clone.PuertoServidor = 1234;
        Assert.Equal(9000, original.PuertoServidor);
    }
}
