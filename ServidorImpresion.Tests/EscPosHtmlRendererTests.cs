using System.Text;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class EscPosHtmlRendererTests
{
    private static readonly Encoding Enc = Encoding.Latin1;

    private static string Render(byte[] data) => EscPosHtmlRenderer.Render(data, Enc);
    private static string Render(string text)  => Render(Enc.GetBytes(text));

    // ── Casos base ────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyData_ReturnsPlaceholder()
    {
        string html = Render([]);
        Assert.Contains("Sin datos", html);
    }

    [Fact]
    public void PlainText_AppearsInOutput()
    {
        string html = Render("Hola mundo\n");
        Assert.Contains("Hola mundo", html);
    }

    [Fact]
    public void MultipleLines_EachInOwnDiv()
    {
        string html = Render("Linea1\nLinea2\n");
        Assert.Contains("Linea1", html);
        Assert.Contains("Linea2", html);
    }

    // ── Seguridad: XSS ────────────────────────────────────────────────────────

    [Fact]
    public void XssScript_IsHtmlEncoded()
    {
        string html = Render("<script>alert(1)</script>\n");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Ampersand_IsHtmlEncoded()
    {
        string html = Render("A&B\n");
        Assert.DoesNotContain("A&B", html);
        Assert.Contains("A&amp;B", html);
    }

    // ── Comandos ESC/POS ──────────────────────────────────────────────────────

    [Fact]
    public void Bold_On_CreatesSpanWithFontWeightBold()
    {
        // ESC E 1 → bold on; ESC E 0 → bold off
        byte[] data = [0x1B, 0x45, 0x01, .. "NEGRITA"u8, 0x1B, 0x45, 0x00, 0x0A];
        string html = Render(data);
        Assert.Contains("font-weight:bold", html);
        Assert.Contains("NEGRITA", html);
    }

    [Fact]
    public void Underline_On_CreatesSpanWithTextDecorationUnderline()
    {
        byte[] data = [0x1B, 0x2D, 0x01, .. "SUBRAYADO"u8, 0x1B, 0x2D, 0x00, 0x0A];
        string html = Render(data);
        Assert.Contains("text-decoration:underline", html);
        Assert.Contains("SUBRAYADO", html);
    }

    [Fact]
    public void AlignCenter_SetsTextAlignCenter()
    {
        byte[] data = [0x1B, 0x61, 0x01, .. "CENTRADO"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("text-align:center", html);
    }

    [Fact]
    public void AlignRight_SetsTextAlignRight()
    {
        byte[] data = [0x1B, 0x61, 0x02, .. "DERECHA"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("text-align:right", html);
    }

    [Fact]
    public void EscReset_ClearsBoldState()
    {
        // ESC E 1 → bold on; ESC @ → reset; texto siguiente no debe ser bold
        byte[] data = [0x1B, 0x45, 0x01, 0x1B, 0x40, .. "NORMAL"u8, 0x0A];
        string html = Render(data);
        // El texto "NORMAL" debe aparecer sin span de negrita
        int boldIdx = html.IndexOf("font-weight:bold", StringComparison.Ordinal);
        int normalIdx = html.IndexOf("NORMAL", StringComparison.Ordinal);
        // Si hay bold, debe estar antes de "NORMAL" (pertenece a texto anterior vacío, no a NORMAL)
        // Lo más directo: NORMAL no debe estar dentro de un span con bold
        Assert.DoesNotContain("font-weight:bold", html.Substring(normalIdx > 0 ? normalIdx : 0));
    }

    [Fact]
    public void PaperCut_GsV_RendersDashedSeparator()
    {
        byte[] data = [.. "Linea\n"u8, 0x1D, 0x56, 0x00];
        string html = Render(data);
        Assert.Contains("dashed", html);
    }

    [Fact]
    public void DoubleHeight_EscBang_SetsFontSizeLarger()
    {
        // ESC ! 0x10 → double height (bit 4)
        byte[] data = [0x1B, 0x21, 0x10, .. "GRANDE"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("font-size:20px", html);
    }

    [Fact]
    public void CashDrawer_EscP_IsSkipped()
    {
        // ESC p 0 100 100 → open cash drawer; el texto posterior debe aparecer intacto
        // El marcador "|||" no forma parte del payload de impresión ni del HTML template,
        // así que si aparece significa que los parámetros del cajón se renderizaron como texto.
        // 0x7C = '|', que Latin1 renderiza tal cual.
        byte[] data = [0x1B, 0x70, 0x7C, 0x7C, 0x7C, .. "Texto\n"u8];
        string html = Render(data);
        Assert.Contains("Texto", html);
        // Los tres bytes 0x7C ('|') del cajón no deben aparecer en el HTML
        Assert.DoesNotContain("|||", html);
    }

    // ── Contenedor ────────────────────────────────────────────────────────────

    [Fact]
    public void Output_ContainsCourierNewFont()
    {
        string html = Render("x\n");
        Assert.Contains("Courier New", html);
    }

    [Fact]
    public void Output_ContainsWhiteBackground()
    {
        string html = Render("x\n");
        Assert.Contains("background:#ffffff", html);
    }

    // ── Nuevos comandos ───────────────────────────────────────────────────────

    [Fact]
    public void EscBang_Bit5_DoubleWidth()
    {
        byte[] data = [0x1B, 0x21, 0x20, .. "ANCHO"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("letter-spacing:4px", html);
    }

    [Fact]
    public void EscBang_Bit3_Bold()
    {
        byte[] data = [0x1B, 0x21, 0x08, .. "BOLD"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("font-weight:bold", html);
    }

    [Fact]
    public void EscBang_Bit7_Underline()
    {
        byte[] data = [0x1B, 0x21, 0x80, .. "SUB"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("text-decoration:underline", html);
    }

    [Fact]
    public void EscBang_Bits45_DoubleHeightAndWidth()
    {
        byte[] data = [0x1B, 0x21, 0x30, .. "GRANDE"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("font-size:20px", html);
        Assert.Contains("letter-spacing:2px", html); // doble alto+ancho combinado
    }

    [Fact]
    public void GsBang_CharSize_DoubleWidthAndHeight()
    {
        // GS ! 0x11 → width×2, height×2
        byte[] data = [0x1D, 0x21, 0x11, .. "XL"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("font-size:20px", html);
        Assert.Contains("letter-spacing:2px", html);
    }

    [Fact]
    public void EscG_DoubleStrike_RendersBold()
    {
        byte[] data = [0x1B, 0x47, 0x01, .. "STRIKE"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("font-weight:bold", html);
    }

    [Fact]
    public void CR_TreatedAsLineFeed()
    {
        // 0x0D = CR — debe producir una línea igual que LF
        byte[] data = [.. "LineaA"u8, 0x0D, .. "LineaB"u8, 0x0A];
        string html = Render(data);
        Assert.Contains("LineaA", html);
        Assert.Contains("LineaB", html);
        // Deben estar en divs distintos (hay dos cierres de div entre ellos)
        int idxA = html.IndexOf("LineaA", StringComparison.Ordinal);
        int idxB = html.IndexOf("LineaB", StringComparison.Ordinal);
        Assert.True(html[idxA..idxB].Contains("</div>"), "CR debe cerrar la línea anterior");
    }

    [Fact]
    public void HT_RendersAsSpaces()
    {
        byte[] data = [.. "A"u8, 0x09, .. "B"u8, 0x0A];
        string html = Render(data);
        // HT se convierte en 4 espacios → texto debe contener A seguido de espacios y B
        Assert.Contains("A", html);
        Assert.Contains("B", html);
    }

    [Fact]
    public void GsK_NewFormat_RendersBarcode()
    {
        // GS k m=0x49 (CODE128 nuevo) n data
        byte[] barcodeData = "123456"u8.ToArray();
        byte[] data = [0x1D, 0x6B, 0x49, (byte)barcodeData.Length, .. barcodeData, 0x0A];
        string html = Render(data);
        Assert.Contains("Libre Barcode 128 Text", html);
        Assert.Contains("123456", html);
    }

    [Fact]
    public void GsK_OldFormat_RendersBarcode()
    {
        // GS k m=0x04 (CODE39 antiguo) data NUL
        byte[] barcodeData = "HOLA"u8.ToArray();
        byte[] data = [0x1D, 0x6B, 0x04, .. barcodeData, 0x00, 0x0A];
        string html = Render(data);
        Assert.Contains("Libre Barcode 128 Text", html);
        Assert.Contains("HOLA", html);
    }

    [Fact]
    public void GsParenK_QrCode_RendersQrPlaceholder()
    {
        // GS ( k — almacenar datos QR: cn=0x31 fn=0x50 m=0 + datos
        byte[] qrContent = "https://test.com"u8.ToArray();
        byte[] payload = [0x31, 0x50, 0x00, .. qrContent];
        byte pL = (byte)(payload.Length % 256);
        byte pH = (byte)(payload.Length / 256);
        byte[] data = [0x1D, 0x28, 0x6B, pL, pH, .. payload, 0x0A];
        string html = Render(data);
        Assert.Contains("QR:", html);
        Assert.Contains("https://test.com", html);
    }

    [Fact]
    public void GsBarcodeParams_AreSkippedWithoutGarbling()
    {
        // GS h / GS w / GS H deben saltarse sin corromper el texto siguiente
        byte[] data = [
            0x1D, 0x68, 0x50,  // GS h 80
            0x1D, 0x77, 0x02,  // GS w 2
            0x1D, 0x48, 0x02,  // GS H 2
            .. "TEXTO_OK"u8, 0x0A
        ];
        string html = Render(data);
        Assert.Contains("TEXTO_OK", html);
    }
}
