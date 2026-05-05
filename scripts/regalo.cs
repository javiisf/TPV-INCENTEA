using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class TicketRegaloScript : ITicketScript
{
    const int MW = 42;
    const int QW = 4;
    const int DW = MW - QW;

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);

        // ── Cabecera empresa ──────────────────────────────────────────────────
        printer.SetJustification(Justify.Center);
        printer.SetTextSize(2, 2);
        WordWrap(printer, (string)empresa.nombreEmpresa, MW / 2);
        printer.SetTextSize(1, 1);
        printer.Feed();
        printer.Text("CIF: " + empresa.cif + "\n");
        WordWrap(printer, (string)empresa.direccion, MW);
        printer.Text(empresa.codPostal + " " + empresa.provincia + "\n");
        printer.Feed(2);

        // ── Título ────────────────────────────────────────────────────────────
        printer.SetBold(true);
        printer.Text("*** TICKET REGALO ***\n");
        printer.SetBold(false);
        printer.Feed();

        printer.SetJustification(Justify.Left);
        printer.Text("Ticket: " + ticket.ticket + "\n");
        printer.Feed();

        // ── Artículos (sin precios) ───────────────────────────────────────────
        printer.SetUnderline(true);
        printer.Text("Ud. ".PadRight(QW) + "Descripción".PadRight(DW) + "\n");
        printer.SetUnderline(false);

        string padQW = new string(' ', QW);

        foreach (var item in ticket.items)
        {
            decimal cant   = (decimal)item.cantidad;
            string  desc   = (string)item.descripcion;
            var     lines  = SplitDesc(desc, DW);

            printer.Text(((int)cant).ToString().PadLeft(QW - 1) + " " + lines[0] + "\n");
            for (int i = 1; i < lines.Count; i++)
                printer.Text(padQW + lines[i] + "\n");
        }

        printer.Separator('=');
        printer.Feed(2);

        // ── Pie ───────────────────────────────────────────────────────────────
        printer.Text("Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "\n");
        printer.Feed();
        printer.SetJustification(Justify.Left);
        WordWrap(printer, (string)empresa.textoLegal, MW);
        printer.Feed();
        printer.SetJustification(Justify.Center);
        printer.Text("Gracias por su visita\n");
        printer.Feed();
        printer.SetBarcodeHeight(80);
        printer.SetBarcodeWidth(2);
        printer.Barcode((string)ticket.ticket);
        printer.Feed();
        printer.Cut();
        printer.Pulse();

        return printer.Close();
    }

    static List<string> SplitDesc(string text, int width)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string current = "";
        foreach (var word in words)
        {
            if (word.Length > width)
            {
                if (current.Length > 0) { lines.Add(current); current = ""; }
                string rest = word;
                while (rest.Length > width) { lines.Add(rest[..(width - 1)] + "-"); rest = rest[(width - 1)..]; }
                current = rest;
            }
            else
            {
                string next = current.Length == 0 ? word : current + " " + word;
                if (next.Length > width) { lines.Add(current); current = word; }
                else current = next;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    static void WordWrap(Printer printer, string texto, int ancho)
    {
        if (string.IsNullOrEmpty(texto)) return;
        var palabras = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string lineaActual = "";
        foreach (var palabra in palabras)
        {
            if (palabra.Length > ancho)
            {
                if (!string.IsNullOrEmpty(lineaActual)) printer.Text(lineaActual + "\n");
                string p = palabra;
                while (p.Length > ancho) { printer.Text(p[..(ancho - 1)] + "-\n"); p = p[(ancho - 1)..]; }
                lineaActual = p;
                continue;
            }
            int largo = string.IsNullOrEmpty(lineaActual) ? palabra.Length : lineaActual.Length + 1 + palabra.Length;
            if (largo <= ancho)
                lineaActual += (string.IsNullOrEmpty(lineaActual) ? "" : " ") + palabra;
            else { printer.Text(lineaActual + "\n"); lineaActual = palabra; }
        }
        if (!string.IsNullOrEmpty(lineaActual)) printer.Text(lineaActual + "\n");
    }
}
