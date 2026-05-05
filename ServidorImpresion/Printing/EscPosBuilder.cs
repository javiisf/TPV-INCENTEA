using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ServidorImpresion
{
    public class EscPosBuilder
    {
        private readonly List<byte> _buffer = new();
        private readonly Encoding _encoding;
        private int _lineWidth;

        // Comandos ESC/POS estándar (Mantenidos todos)
        private static readonly byte[] CmdInitialize = [0x1B, 0x40];           // ESC @
        private static readonly byte[] CmdCodePage858 = [0x1B, 0x74, 0x13];  // ESC t 19 (CP858 = CP850 + €)
        private static readonly byte[] CmdBoldOn = [0x1B, 0x45, 0x01];         // ESC E 1
        private static readonly byte[] CmdBoldOff = [0x1B, 0x45, 0x00];        // ESC E 0
        private static readonly byte[] CmdUnderlineOn = [0x1B, 0x2D, 0x01];    // ESC - 1
        private static readonly byte[] CmdUnderlineOff = [0x1B, 0x2D, 0x00];   // ESC - 0
        private static readonly byte[] CmdAlignLeft = [0x1B, 0x61, 0x00];      // ESC a 0
        private static readonly byte[] CmdAlignCenter = [0x1B, 0x61, 0x01];    // ESC a 1
        private static readonly byte[] CmdAlignRight = [0x1B, 0x61, 0x02];     // ESC a 2
        private static readonly byte[] CmdDoubleHeightOn = [0x1B, 0x21, 0x10]; // ESC ! 0x10
        private static readonly byte[] CmdDoubleHeightOff = [0x1B, 0x21, 0x00];// ESC ! 0x00
        private static readonly byte[] CmdCutPaper = [0x1D, 0x56, 0x00];       // GS V 0 (corte total)
        private static readonly byte[] CmdCutPartial = [0x1D, 0x56, 0x01];     // GS V 1 (corte parcial)
        private static readonly byte[] CmdOpenDrawer = [0x1B, 0x70, 0x00, 0x19, 0x78]; // ESC p 0 25 120
        private static readonly byte[] CmdLineFeed = [0x0A];                    // LF

        private static readonly byte[] CmdDoubleSizeOn  = [0x1B, 0x21, 0x30]; // ESC ! double width+height
        private static readonly byte[] CmdDoubleWidthOn  = [0x1B, 0x21, 0x20]; // ESC ! double width only
        private static readonly byte[] CmdFontA         = [0x1B, 0x4D, 0x00]; // ESC M 0 (Font A, normal)
        private static readonly byte[] CmdFontB         = [0x1B, 0x4D, 0x01]; // ESC M 1 (Font B, condensada)
        private static readonly byte[] CmdBarcodeHeight = [0x1D, 0x68, 0x50]; // GS h 80
        private static readonly byte[] CmdBarcodeWidth  = [0x1D, 0x77, 0x02]; // GS w 2

        public EscPosBuilder(Encoding encoding, int lineWidth = 48)
        {
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _lineWidth = lineWidth;
        }

        public EscPosBuilder Initialize()
        {
            _buffer.AddRange(CmdInitialize);
            _buffer.AddRange(CmdCodePage858);
            return this;
        }

        public EscPosBuilder Text(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            _buffer.AddRange(_encoding.GetBytes(text));
            return this;
        }

        public EscPosBuilder TextLine(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            _buffer.AddRange(_encoding.GetBytes(text));
            _buffer.AddRange(CmdLineFeed);
            return this;
        }

        public EscPosBuilder LineFeed(int count = 1)
        {
            for (int i = 0; i < count; i++)
                _buffer.AddRange(CmdLineFeed);
            return this;
        }

        // --- Formato ---
        public EscPosBuilder SetFont(int font) { _buffer.AddRange(font == 1 ? CmdFontB : CmdFontA); return this; }
        public EscPosBuilder BoldOn() { _buffer.AddRange(CmdBoldOn); return this; }
        public EscPosBuilder BoldOff() { _buffer.AddRange(CmdBoldOff); return this; }
        public EscPosBuilder UnderlineOn() { _buffer.AddRange(CmdUnderlineOn); return this; }
        public EscPosBuilder UnderlineOff() { _buffer.AddRange(CmdUnderlineOff); return this; }
        public EscPosBuilder DoubleHeightOn() { _buffer.AddRange(CmdDoubleHeightOn); return this; }
        public EscPosBuilder DoubleHeightOff() { _buffer.AddRange(CmdDoubleHeightOff); return this; }
        public EscPosBuilder DoubleSizeOn() { _buffer.AddRange(CmdDoubleSizeOn); return this; }
        public EscPosBuilder DoubleWidthOn() { _buffer.AddRange(CmdDoubleWidthOn); return this; }

        // --- Alineación ---
        public EscPosBuilder AlignLeft() { _buffer.AddRange(CmdAlignLeft); return this; }
        public EscPosBuilder AlignCenter() { _buffer.AddRange(CmdAlignCenter); return this; }
        public EscPosBuilder AlignRight() { _buffer.AddRange(CmdAlignRight); return this; }

        // --- Utilidades de ticket ---

        public EscPosBuilder Separator(char ch = '-')
        {
            _buffer.AddRange(_encoding.GetBytes(new string(ch, _lineWidth)));
            _buffer.AddRange(CmdLineFeed);
            return this;
        }

        public EscPosBuilder LeftRight(string left, string right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            int rightByteLen = _encoding.GetByteCount(right);
            int leftByteLen = _encoding.GetByteCount(left);

            while (rightByteLen > _lineWidth && right.Length > 0)
            {
                right = right[..^1];
                rightByteLen = _encoding.GetByteCount(right);
            }

            int maxLeftBytes = _lineWidth - rightByteLen - 1;
            if (maxLeftBytes < 0) maxLeftBytes = 0;

            while (leftByteLen > maxLeftBytes && left.Length > 0)
            {
                left = left[..^1];
                leftByteLen = _encoding.GetByteCount(left);
            }

            int spaces = _lineWidth - leftByteLen - rightByteLen;
            if (spaces < 1) spaces = 1;

            string line = left + new string(' ', spaces) + right;
            byte[] lineBytes = _encoding.GetBytes(line);

            if (lineBytes.Length > _lineWidth)
                _buffer.AddRange(lineBytes.AsSpan(0, _lineWidth));
            else
            {
                _buffer.AddRange(lineBytes);
                for (int i = lineBytes.Length; i < _lineWidth; i++)
                    _buffer.Add(0x20);
            }

            _buffer.AddRange(CmdLineFeed);
            return this;
        }

        public EscPosBuilder Currency(string label, decimal amount, string symbol = "€", CultureInfo? culture = null)
        {
            culture ??= new CultureInfo("es-ES");
            string formatted = amount.ToString("N2", culture) + " " + symbol;
            return LeftRight(label, formatted);
        }

        public EscPosBuilder DateTime(string label, System.DateTime dateTime, string format = "dd/MM/yyyy HH:mm", CultureInfo? culture = null)
        {
            culture ??= new CultureInfo("es-ES");
            string formatted = dateTime.ToString(format, culture);
            return LeftRight(label, formatted);
        }

        public EscPosBuilder Barcode(string data)
        {
            if (string.IsNullOrEmpty(data)) return this;
            _buffer.AddRange(CmdBarcodeHeight);
            _buffer.AddRange(CmdBarcodeWidth);
            _buffer.AddRange([0x1D, 0x6B, 0x04]); // GS k 4 (CODE39, formato clásico compatible con todos ESC/POS)
            _buffer.AddRange(_encoding.GetBytes(data.ToUpperInvariant())); // CODE39 solo acepta mayúsculas
            _buffer.Add(0x00); // NUL terminator — requerido por formato antiguo
            return this;
        }

        public EscPosBuilder WordWrap(string text, int width = 0)
        {
            ArgumentNullException.ThrowIfNull(text);
            int w = width > 0 ? width : _lineWidth;
            text = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
            string[] words = text.Split(' ');
            string line = "";
            foreach (string word in words)
            {
                if (word.Length > w)
                {
                    if (line.Length > 0) { TextLine(line); line = ""; }
                    string rest = word;
                    while (rest.Length > w) { TextLine(rest[..(w - 1)] + "-"); rest = rest[(w - 1)..]; }
                    line = rest;
                }
                else if ((line.Length == 0 ? word : line + " " + word).Length > w)
                {
                    TextLine(line);
                    line = word;
                }
                else
                {
                    line = line.Length == 0 ? word : line + " " + word;
                }
            }
            if (line.Length > 0) TextLine(line);
            return this;
        }

        public EscPosBuilder Cut() { _buffer.AddRange(CmdCutPaper); return this; }
        public EscPosBuilder CutPartial() { _buffer.AddRange(CmdCutPartial); return this; }
        public EscPosBuilder OpenCashDrawer() { _buffer.AddRange(CmdOpenDrawer); return this; }

        public EscPosBuilder Raw(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            _buffer.AddRange(data);
            return this;
        }

        public byte[] Build()
        {
            if (_buffer.Count == 0) return Array.Empty<byte>();
            return _buffer.ToArray();
        }
    }
}