using System.Text;

namespace ServidorImpresion
{
    public enum Justify { Left, Center, Right }

    /// <summary>
    /// Wrapper sobre EscPosBuilder con una API similar a la librería PHP mike42/escpos-php.
    /// </summary>
    public sealed class Printer
    {
        private readonly EscPosBuilder _b;

        public Printer(Encoding encoding, int lineWidth = 47)
        {
            _b = new EscPosBuilder(encoding, lineWidth).Initialize();
        }

        public Printer SetJustification(Justify j)
        {
            switch (j)
            {
                case Justify.Center: _b.AlignCenter(); break;
                case Justify.Right:  _b.AlignRight();  break;
                default:             _b.AlignLeft();   break;
            }
            return this;
        }

        public Printer SetTextSize(int width, int height)
        {
            if (width >= 2 && height >= 2) _b.DoubleSizeOn();
            else if (width >= 2)           _b.DoubleWidthOn();
            else if (height >= 2)          _b.DoubleHeightOn();
            else                           _b.DoubleHeightOff();
            return this;
        }

        public Printer SetFont(int font)           { _b.SetFont(font); return this; }
        public Printer SetBold(bool on)           { if (on) _b.BoldOn(); else _b.BoldOff(); return this; }
        public Printer SetUnderline(bool on)      { if (on) _b.UnderlineOn(); else _b.UnderlineOff(); return this; }
        public Printer Text(string text)          { _b.Text(text); return this; }
        public Printer Feed(int lines = 1)        { _b.LineFeed(lines); return this; }
        public Printer Separator(char ch = '=')   { _b.Separator(ch); return this; }
        public Printer SetBarcodeHeight(int pts)  { _b.Raw([0x1D, 0x68, (byte)Math.Clamp(pts, 1, 255)]); return this; }
        public Printer SetBarcodeWidth(int width) { _b.Raw([0x1D, 0x77, (byte)Math.Clamp(width, 1, 6)]); return this; }
        public Printer Barcode(string data)       { _b.Barcode(data); return this; }
        public Printer Cut()                  { _b.Cut(); return this; }
        public Printer Pulse()                { _b.OpenCashDrawer(); return this; }

        public byte[] Close() => _b.Build();
    }
}
