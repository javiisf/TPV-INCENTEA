using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ServidorImpresion
{
    public static class EscPosHtmlRenderer
    {
        private readonly record struct Segment(string Text, bool Bold, bool Underline, bool DoubleHeight, bool DoubleWidth);

        private enum LineKind { Text, Cut, Barcode, Qr }
        private readonly record struct Line(List<Segment> Segments, int Align, LineKind Kind);

        public static string Render(byte[] data, Encoding encoding, int charsPerLine = 42)
        {
            if (charsPerLine < 24 || charsPerLine > 80) charsPerLine = 42;

            if (data == null || data.Length == 0)
                return "<div style='font-family:monospace;color:#aaa;font-size:11px;'>Sin datos</div>";

            bool bold = false, underline = false, doubleHeight = false, doubleWidth = false;
            int align = 0;

            var lines = new List<Line>();
            var currentSegments = new List<Segment>();
            var textBytes = new List<byte>();

            void FlushText()
            {
                if (textBytes.Count == 0) return;
                string text = encoding.GetString(textBytes.ToArray());
                textBytes.Clear();
                currentSegments.Add(new Segment(text, bold, underline, doubleHeight, doubleWidth));
            }

            void FlushLine()
            {
                FlushText();
                lines.Add(new Line(new List<Segment>(currentSegments), align, LineKind.Text));
                currentSegments.Clear();
            }

            void AddBarcode(byte[] barcodeData, LineKind kind)
            {
                FlushLine();
                string txt = encoding.GetString(barcodeData);
                lines.Add(new Line(new List<Segment> { new Segment(txt, false, false, false, false) }, align, kind));
            }

            int i = 0;
            while (i < data.Length)
            {
                byte b = data[i];

                if (b == 0x1B) // ESC
                {
                    FlushText();
                    i++;
                    if (i >= data.Length) break;
                    byte cmd = data[i++];

                    switch (cmd)
                    {
                        case 0x40: // ESC @ — reset
                            bold = underline = doubleHeight = doubleWidth = false;
                            align = 0;
                            break;

                        case 0x45: // ESC E n — negrita
                            if (i < data.Length) bold = data[i++] != 0;
                            break;

                        case 0x2D: // ESC - n — subrayado
                            if (i < data.Length) underline = data[i++] != 0;
                            break;

                        case 0x61: // ESC a n — alineación
                            if (i < data.Length) align = data[i++] & 0x03;
                            break;

                        case 0x21: // ESC ! n — modos combinados
                            if (i < data.Length)
                            {
                                byte n = data[i++];
                                bold        = (n & 0x08) != 0; // bit 3
                                doubleHeight = (n & 0x10) != 0; // bit 4
                                doubleWidth  = (n & 0x20) != 0; // bit 5
                                underline   = (n & 0x80) != 0; // bit 7
                            }
                            break;

                        case 0x47: // ESC G n — doble impresión (mismo efecto visual que negrita)
                            if (i < data.Length) bold = data[i++] != 0;
                            break;

                        case 0x70: // ESC p — abrir cajón (3 bytes de parámetros)
                            i = Math.Min(i + 3, data.Length);
                            break;

                        case 0x32: // ESC 2 — interlineado estándar (sin parámetro, ignorar)
                        case 0x4D: // ESC M n — selección de fuente (1 byte)
                            if (cmd == 0x4D && i < data.Length) i++;
                            break;

                        case 0x33: // ESC 3 n — interlineado personalizado (1 byte)
                        case 0x41: // ESC A n — interlineado (1 byte)
                            if (i < data.Length) i++;
                            break;
                    }
                }
                else if (b == 0x1D) // GS
                {
                    FlushText();
                    i++;
                    if (i >= data.Length) break;
                    byte cmd = data[i++];

                    switch (cmd)
                    {
                        case 0x56: // GS V — corte
                            if (i < data.Length) i++; // parámetro
                            FlushLine();
                            lines.Add(new Line(new List<Segment>(), Align: 1, LineKind.Cut));
                            break;

                        case 0x21: // GS ! n — tamaño de carácter
                            // bits 4-6: multiplicador ancho (0=×1 … 7=×8)
                            // bits 0-2: multiplicador alto  (0=×1 … 7=×8)
                            if (i < data.Length)
                            {
                                byte n = data[i++];
                                doubleWidth  = (n & 0x70) != 0;
                                doubleHeight = (n & 0x07) != 0;
                            }
                            break;

                        case 0x6B: // GS k — código de barras
                        {
                            if (i >= data.Length) break;
                            byte m = data[i++];

                            if (m >= 0x41) // formato nuevo: m ≥ 0x41, sigue byte de longitud
                            {
                                if (i >= data.Length) break;
                                byte len = data[i++];
                                int end = Math.Min(i + len, data.Length);
                                AddBarcode(data[i..end], LineKind.Barcode);
                                i = end;
                            }
                            else // formato antiguo: datos terminados en NUL
                            {
                                int start = i;
                                while (i < data.Length && data[i] != 0x00) i++;
                                AddBarcode(data[start..i], LineKind.Barcode);
                                if (i < data.Length) i++; // saltar NUL
                            }
                            break;
                        }

                        case 0x28: // GS ( — comandos extendidos
                        {
                            if (i >= data.Length) break;
                            byte fn_group = data[i++];

                            if (fn_group == 0x6B) // GS ( k — QR code
                            {
                                if (i + 1 >= data.Length) break;
                                int paramLen = data[i] + data[i + 1] * 256;
                                i += 2;
                                int dataEnd = Math.Min(i + paramLen, data.Length);

                                // cn=49 (0x31), fn=80 (0x50) = almacenar datos del QR
                                if (paramLen >= 3 && i < dataEnd && data[i] == 0x31 && (i + 1) < dataEnd && data[i + 1] == 0x50)
                                {
                                    // estructura: cn fn m data...  (m suele ser 0)
                                    int qrStart = i + 3;
                                    if (qrStart < dataEnd)
                                        AddBarcode(data[qrStart..dataEnd], LineKind.Qr);
                                }
                                i = dataEnd;
                            }
                            else // otro GS ( : leer longitud y saltar
                            {
                                if (i + 1 >= data.Length) break;
                                int paramLen = data[i] + data[i + 1] * 256;
                                i = Math.Min(i + 2 + paramLen, data.Length);
                            }
                            break;
                        }

                        // GS h / GS w / GS H / GS f / GS x — parámetros de barcode (1 byte cada uno)
                        case 0x68: case 0x77: case 0x48: case 0x66: case 0x78:
                            if (i < data.Length) i++;
                            break;
                    }
                }
                else if (b == 0x0A || b == 0x0D) { FlushLine(); i++; } // LF / CR
                else if (b == 0x09) { textBytes.Add(0x20); textBytes.Add(0x20); textBytes.Add(0x20); textBytes.Add(0x20); i++; } // HT → 4 espacios
                else if (b >= 0x20) { textBytes.Add(b); i++; }
                else i++;
            }

            FlushText();
            if (currentSegments.Count > 0)
                lines.Add(new Line(currentSegments, align, LineKind.Text));

            return BuildHtml(lines, charsPerLine);
        }

        private static string BuildHtml(List<Line> lines, int charsPerLine)
        {
            var sb = new StringBuilder();
            sb.Append(
                "<div style='" +
                "font-family:\"Lucida Console\", \"Courier New\", monospace;" +
                "font-size:12px;" +
                "background:#ffffff;" +
                "color:#000000;" +
                "padding:20px 15px;" +
                "border:1px solid #ddd;" +
                $"width:{charsPerLine}ch;" +
                "margin: 0;" +
                "box-shadow: 0 2px 5px rgba(0,0,0,0.1);" +
                "box-sizing: content-box;" +
                "display: block;" +
                "overflow: hidden;" +
                "'>");

            foreach (var line in lines)
            {
                switch (line.Kind)
                {
                    case LineKind.Cut:
                        sb.Append("<div style='text-align:left;color:#bbb;letter-spacing:1px;border-top:1px dashed #ddd;margin:12px 0;padding-top:4px;font-size:12px;'>&#x2702; - - - - - - - - - - - - - - - - - -</div>");
                        continue;

                    case LineKind.Barcode:
                        sb.Append("<div style='text-align:center;margin:10px 0;'>");
                        sb.Append($"<div style='font-family:\"Libre Barcode 128 Text\",monospace;font-size:48px;line-height:1;'>{WebUtility.HtmlEncode(line.Segments[0].Text)}</div>");
                        sb.Append($"<div style='font-size:10px;margin-top:2px;'>{WebUtility.HtmlEncode(line.Segments[0].Text)}</div></div>");
                        continue;

                    case LineKind.Qr:
                        sb.Append("<div style='text-align:center;margin:10px 0;'>");
                        sb.Append("<div style='display:inline-block;font-size:32px;line-height:1;'>&#x2588;&#x2591;&#x2588;&#x2591;&#x2588;<br/>&#x2591;&#x2588;&#x2591;&#x2588;&#x2591;<br/>&#x2588;&#x2591;&#x2588;&#x2591;&#x2588;</div>");
                        sb.Append($"<div style='font-size:10px;margin-top:4px;'>QR: {WebUtility.HtmlEncode(line.Segments[0].Text)}</div></div>");
                        continue;
                }

                // LineKind.Text
                string[] alignValues = ["left", "center", "right"];
                string alignStyle = $"text-align:{alignValues[Math.Clamp(line.Align, 0, 2)]};";
                sb.Append($"<div style='{alignStyle}white-space:pre;line-height:1.2;margin:0;width:100%;'>");

                if (line.Segments.Count == 0) sb.Append("&nbsp;");
                else
                {
                    foreach (var seg in line.Segments)
                    {
                        string encoded = WebUtility.HtmlEncode(seg.Text);
                        var style = new StringBuilder();
                        if (seg.Bold) style.Append("font-weight:bold;");
                        if (seg.Underline) style.Append("text-decoration:underline;");
                        if (seg.DoubleHeight && seg.DoubleWidth) style.Append("font-size:20px;line-height:1.1;letter-spacing:2px;");
                        else if (seg.DoubleHeight) style.Append("font-size:20px;line-height:1.1;");
                        else if (seg.DoubleWidth) style.Append("letter-spacing:4px;");

                        if (style.Length > 0) sb.Append($"<span style='{style}'>{encoded}</span>");
                        else sb.Append(encoded);
                    }
                }
                sb.Append("</div>");
            }
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
