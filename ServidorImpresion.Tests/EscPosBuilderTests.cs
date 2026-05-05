using System;
using System.Text;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class EscPosBuilderTests
{
    private static readonly Encoding Enc = Encoding.ASCII;

    // ── Build vacío ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_EmptyBuilder_ReturnsEmptyArray()
    {
        var result = new EscPosBuilder(Enc).Build();
        Assert.Empty(result);
    }

    // ── Comandos de control ───────────────────────────────────────────────────

    [Fact]
    public void Initialize_EmitsEscAt()
    {
        var result = new EscPosBuilder(Enc).Initialize().Build();
        Assert.Equal(new byte[] { 0x1B, 0x40 }, result);
    }

    [Fact]
    public void Cut_EmitsGsV0()
    {
        var result = new EscPosBuilder(Enc).Cut().Build();
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x00 }, result);
    }

    [Fact]
    public void CutPartial_EmitsGsV1()
    {
        var result = new EscPosBuilder(Enc).CutPartial().Build();
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x01 }, result);
    }

    [Fact]
    public void OpenCashDrawer_EmitsEscP()
    {
        var result = new EscPosBuilder(Enc).OpenCashDrawer().Build();
        Assert.Equal(new byte[] { 0x1B, 0x70, 0x00, 0x19, 0x78 }, result);
    }

    // ── Texto ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_AppendsEncodedBytes()
    {
        var result = new EscPosBuilder(Enc).Text("AB").Build();
        Assert.Equal(new byte[] { (byte)'A', (byte)'B' }, result);
    }

    [Fact]
    public void Text_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new EscPosBuilder(Enc).Text(null!));
    }

    [Fact]
    public void TextLine_AppendsTextPlusLF()
    {
        var result = new EscPosBuilder(Enc).TextLine("AB").Build();
        Assert.Equal(new byte[] { (byte)'A', (byte)'B', 0x0A }, result);
    }

    [Fact]
    public void TextLine_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new EscPosBuilder(Enc).TextLine(null!));
    }

    [Fact]
    public void LineFeed_Default_EmitsOneLF()
    {
        var result = new EscPosBuilder(Enc).LineFeed().Build();
        Assert.Equal(new byte[] { 0x0A }, result);
    }

    [Fact]
    public void LineFeed_Count3_EmitsThreeLF()
    {
        var result = new EscPosBuilder(Enc).LineFeed(3).Build();
        Assert.Equal(new byte[] { 0x0A, 0x0A, 0x0A }, result);
    }

    // ── Formato ───────────────────────────────────────────────────────────────

    [Fact]
    public void BoldOn_EmitsEscE1()
    {
        var result = new EscPosBuilder(Enc).BoldOn().Build();
        Assert.Equal(new byte[] { 0x1B, 0x45, 0x01 }, result);
    }

    [Fact]
    public void BoldOff_EmitsEscE0()
    {
        var result = new EscPosBuilder(Enc).BoldOff().Build();
        Assert.Equal(new byte[] { 0x1B, 0x45, 0x00 }, result);
    }

    [Fact]
    public void UnderlineOn_EmitsEscMinus1()
    {
        var result = new EscPosBuilder(Enc).UnderlineOn().Build();
        Assert.Equal(new byte[] { 0x1B, 0x2D, 0x01 }, result);
    }

    [Fact]
    public void UnderlineOff_EmitsEscMinus0()
    {
        var result = new EscPosBuilder(Enc).UnderlineOff().Build();
        Assert.Equal(new byte[] { 0x1B, 0x2D, 0x00 }, result);
    }

    [Fact]
    public void DoubleHeightOn_EmitsEscExclamation10()
    {
        var result = new EscPosBuilder(Enc).DoubleHeightOn().Build();
        Assert.Equal(new byte[] { 0x1B, 0x21, 0x10 }, result);
    }

    [Fact]
    public void DoubleHeightOff_EmitsEscExclamation00()
    {
        var result = new EscPosBuilder(Enc).DoubleHeightOff().Build();
        Assert.Equal(new byte[] { 0x1B, 0x21, 0x00 }, result);
    }

    // ── Alineación ────────────────────────────────────────────────────────────

    [Fact]
    public void AlignLeft_EmitsEscA0()
    {
        var result = new EscPosBuilder(Enc).AlignLeft().Build();
        Assert.Equal(new byte[] { 0x1B, 0x61, 0x00 }, result);
    }

    [Fact]
    public void AlignCenter_EmitsEscA1()
    {
        var result = new EscPosBuilder(Enc).AlignCenter().Build();
        Assert.Equal(new byte[] { 0x1B, 0x61, 0x01 }, result);
    }

    [Fact]
    public void AlignRight_EmitsEscA2()
    {
        var result = new EscPosBuilder(Enc).AlignRight().Build();
        Assert.Equal(new byte[] { 0x1B, 0x61, 0x02 }, result);
    }

    // ── Bytes crudos ─────────────────────────────────────────────────────────

    [Fact]
    public void Raw_AppendsBytesDirectly()
    {
        var raw = new byte[] { 0xAA, 0xBB, 0xCC };
        var result = new EscPosBuilder(Enc).Raw(raw).Build();
        Assert.Equal(raw, result);
    }

    [Fact]
    public void Raw_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new EscPosBuilder(Enc).Raw(null!));
    }

    // ── Separador ─────────────────────────────────────────────────────────────

    [Fact]
    public void Separator_DefaultDash_EmitsLineWidthDashesAndLF()
    {
        const int width = 10;
        var result = new EscPosBuilder(Enc, width).Separator().Build();

        Assert.Equal(width + 1, result.Length);
        for (int i = 0; i < width; i++)
            Assert.Equal((byte)'-', result[i]);
        Assert.Equal(0x0A, result[width]);
    }

    [Fact]
    public void Separator_CustomChar_EmitsCorrectChar()
    {
        const int width = 5;
        var result = new EscPosBuilder(Enc, width).Separator('=').Build();
        for (int i = 0; i < width; i++)
            Assert.Equal((byte)'=', result[i]);
    }

    // ── LeftRight ─────────────────────────────────────────────────────────────

    [Fact]
    public void LeftRight_FitsLine_CorrectLengthAndContent()
    {
        const int width = 20;
        var result = new EscPosBuilder(Enc, width).LeftRight("LEFT", "RIGHT").Build();

        // El resultado es la línea (20 bytes) + LF
        Assert.Equal(width + 1, result.Length);
        Assert.Equal(0x0A, result[width]);

        string line = Enc.GetString(result, 0, width);
        Assert.StartsWith("LEFT", line);
        Assert.EndsWith("RIGHT", line);
    }

    [Fact]
    public void LeftRight_TotalWidthIsLineWidth()
    {
        const int width = 30;
        var result = new EscPosBuilder(Enc, width).LeftRight("LABEL", "VALUE").Build();

        // La línea ocupa exactamente _lineWidth bytes (sin el LF)
        Assert.Equal(width + 1, result.Length);
    }

    [Fact]
    public void LeftRight_NullLeftThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new EscPosBuilder(Enc, 20).LeftRight(null!, "R"));
    }

    [Fact]
    public void LeftRight_NullRightThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new EscPosBuilder(Enc, 20).LeftRight("L", null!));
    }

    // ── Currency ──────────────────────────────────────────────────────────────

    [Fact]
    public void Currency_LabelAppearsAtStart()
    {
        var result = new EscPosBuilder(Enc, 48).Currency("TOTAL", 6.80m, "EUR").Build();
        string line = Enc.GetString(result, 0, 48);
        Assert.StartsWith("TOTAL", line);
    }

    [Fact]
    public void Currency_AmountAppearsAtEnd()
    {
        var result = new EscPosBuilder(Enc, 48).Currency("TOTAL", 6.80m, "EUR").Build();
        string line = Enc.GetString(result, 0, 48);
        // El importe formateado en es-ES termina con el símbolo
        Assert.EndsWith("EUR", line.TrimEnd());
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void NullEncoding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EscPosBuilder(null!));
    }

    // ── Encadenamiento fluido ─────────────────────────────────────────────────

    [Fact]
    public void FluentChain_InitializeBoldTextCut_CorrectBytes()
    {
        var result = new EscPosBuilder(Enc)
            .Initialize()   // ESC @
            .BoldOn()       // ESC E 1
            .Text("X")      // 'X'
            .BoldOff()      // ESC E 0
            .Cut()          // GS V 0
            .Build();

        var expected = new byte[]
        {
            0x1B, 0x40,             // ESC @
            0x1B, 0x45, 0x01,       // ESC E 1
            (byte)'X',              // 'X'
            0x1B, 0x45, 0x00,       // ESC E 0
            0x1D, 0x56, 0x00        // GS V 0
        };
        Assert.Equal(expected, result);
    }
}
