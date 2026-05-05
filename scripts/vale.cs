using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class ValeScript : ITicketScript
{
    const int MW = 42;

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);

        // ── Cabecera ──────────────────────────────────────────────────────────
        printer.SetJustification(Justify.Center);
        printer.SetTextSize(2, 2);
        WordWrap(printer, (string)empresa.nombreEmpresa, MW / 2);
        printer.SetTextSize(1, 1);
        printer.Feed();
        printer.Text("CIF: " + empresa.cif + "\n");
        WordWrap(printer, (string)empresa.direccion, MW);
        printer.Text(empresa.codPostal + " " + empresa.provincia + "\n");
        printer.Feed(2);

        printer.SetJustification(Justify.Left);
        printer.Text("Vale num.: " + ticket.vale + "\n");
        printer.Feed();

        // ── Importe del Vale ──────────────────────────────────────────────────
        printer.SetJustification(Justify.Center);
        printer.SetTextSize(2, 2);
        printer.Text("VALE: " + ((decimal)ticket.total).ToString("N2") + " EUR\n");
        printer.SetTextSize(1, 1);
        printer.Feed(2);

        // ── Pie y Legal ───────────────────────────────────────────────────────
        printer.SetJustification(Justify.Left);
        printer.Text("Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "\n");
        printer.Feed();
        WordWrap(printer, (string)empresa.textoLegal, MW);
        printer.Feed(2);

        printer.SetJustification(Justify.Center);
        printer.Text("Gracias por su visita\n");
        printer.Feed();

        // Código de barras (CODE39)
        printer.SetBarcodeHeight(80);
        printer.SetBarcodeWidth(2);
        printer.Barcode((string)ticket.vale);
        
        printer.Feed(2);
        printer.Cut();
        printer.Pulse();

        return printer.Close();
    }

    static void WordWrap(Printer printer, string texto, int ancho) {
        if (string.IsNullOrEmpty(texto)) return;
        var palabras = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string lineaActual = "";
        foreach (var palabra in palabras) {
            if (palabra.Length > ancho) {
                if (!string.IsNullOrEmpty(lineaActual)) printer.Text(lineaActual + "\n");
                string p = palabra;
                while (p.Length > ancho) { printer.Text(p[..(ancho - 1)] + "-\n"); p = p[(ancho - 1)..]; }
                lineaActual = p;
                continue;
            }
            int largo = string.IsNullOrEmpty(lineaActual) ? palabra.Length : lineaActual.Length + 1 + palabra.Length;
            if (largo <= ancho) lineaActual += (string.IsNullOrEmpty(lineaActual) ? "" : " ") + palabra;
            else { printer.Text(lineaActual + "\n"); lineaActual = palabra; }
        }
        if (!string.IsNullOrEmpty(lineaActual)) printer.Text(lineaActual + "\n");
    }
}