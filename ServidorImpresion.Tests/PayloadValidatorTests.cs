using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class PayloadValidatorTests
{
    // ── Casos que deben rechazarse ────────────────────────────────────────────

    [Fact]
    public void Empty_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([]));

    [Fact]
    public void WindowsPE_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([0x4D, 0x5A, 0x00, 0x00]));

    [Fact]
    public void Elf_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([0x7F, 0x45, 0x4C, 0x46]));

    [Fact]
    public void Pdf_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([0x25, 0x50, 0x44, 0x46, 0x2D]));

    [Fact]
    public void Zip_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([0x50, 0x4B, 0x03, 0x04]));

    [Fact]
    public void Jpeg_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([0xFF, 0xD8, 0xFF, 0xE0]));

    [Fact]
    public void Png_ReturnsFalse()
        => Assert.False(PayloadValidator.IsValidPrinterPayload([0x89, 0x50, 0x4E, 0x47]));

    [Fact]
    public void NullByte_ReturnsFalse()
    {
        byte[] data = [0x41, 0x42, 0x00, 0x43]; // ABC\0C
        Assert.False(PayloadValidator.IsValidPrinterPayload(data));
    }

    [Fact]
    public void LongTextWithoutNewline_ReturnsFalse()
    {
        // Más de 128 bytes de texto sin ningún LF/CR
        byte[] data = new byte[200];
        for (int i = 0; i < 200; i++) data[i] = 0x41; // 'A'
        Assert.False(PayloadValidator.IsValidPrinterPayload(data));
    }

    // ── Casos que deben aceptarse ─────────────────────────────────────────────

    [Fact]
    public void EscPos_StartsWithEsc_ReturnsTrue()
        => Assert.True(PayloadValidator.IsValidPrinterPayload([0x1B, 0x40])); // ESC @

    [Fact]
    public void EscPos_StartsWithGs_ReturnsTrue()
        => Assert.True(PayloadValidator.IsValidPrinterPayload([0x1D, 0x56, 0x00])); // GS V

    [Fact]
    public void Zpl_CaretPrefix_ReturnsTrue()
        => Assert.True(PayloadValidator.IsValidPrinterPayload("^XA^FO50,50^ADN,36,20^FDHola^FS^XZ"u8.ToArray()));

    [Fact]
    public void Zpl_TildePrefix_ReturnsTrue()
        => Assert.True(PayloadValidator.IsValidPrinterPayload("~TA000"u8.ToArray()));

    [Fact]
    public void PlainTextWithNewline_ReturnsTrue()
    {
        byte[] data = "Ticket de venta\nProducto A   5.00\nTotal        5.00\n"u8.ToArray();
        Assert.True(PayloadValidator.IsValidPrinterPayload(data));
    }

    [Fact]
    public void ShortText_ReturnsTrue()
        => Assert.True(PayloadValidator.IsValidPrinterPayload("OK"u8.ToArray()));
}
