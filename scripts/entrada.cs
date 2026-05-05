using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class EntradaCajaScript : ITicketScript
{
    const int MW = 42;

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);

        // El PHP original realiza un bucle de 2 iteraciones para imprimir dos copias
        for (int i = 1; i <= 2; i++)
        {
            // ── Cabecera ──────────────────────────────────────────────────────
            printer.SetJustification(Justify.Center);
            printer.SetTextSize(2, 2);
            WordWrap(printer, (string)empresa.nombreEmpresa, MW / 2);
            
            printer.SetTextSize(1, 1);
            printer.Feed();
            printer.Text("CIF: " + empresa.cif + "\n");
            WordWrap(printer, (string)empresa.direccion, MW);
            printer.Text(empresa.codPostal + " " + empresa.provincia + "\n");
            printer.Feed(2);

            // ── Cuerpo del Movimiento ─────────────────────────────────────────
            printer.SetJustification(Justify.Left);
            printer.SetBold(true);
            printer.Text("ENTRADA EN CAJA\n");
            printer.SetBold(false);
            
            // Usuario que realiza la operación
            printer.Text("USUARIO: " + ticket.usuario + "\n");
            
            // Concepto (opcional, si el JSON lo incluye)
            if (Has(ticket, "concepto")) 
            {
                WordWrap(printer, "CONCEPTO: " + (string)ticket.concepto, MW);
            }
            printer.Feed();

            // ── Importe ───────────────────────────────────────────────────────
            printer.SetJustification(Justify.Center);
            printer.SetTextSize(2, 2);
            // Se usa cantidadEntrada según la estructura de tu PHP
            printer.Text("ENTRADA: " + ((decimal)ticket.cantidadEntrada).ToString("N2") + " EUR\n");
            printer.SetTextSize(1, 1);
            printer.Feed(2);

            // ── Pie de copia ──────────────────────────────────────────────────
            printer.SetJustification(Justify.Left);
            printer.Text("FIRMADO:\n");
            printer.Feed(3);
            printer.Text("Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "\n");
            printer.Feed(2);

            // Separador visual entre copias (solo si no es la última)
            if (i == 1) 
            {
                printer.Separator('-');
                printer.Feed(3);
            }
        }

        printer.Cut();
        printer.Pulse();

        return printer.Close();
    }

    // --- Métodos auxiliares ---

    static bool Has(dynamic obj, string key) 
    {
        var d = (IDictionary<string, object?>)obj;
        return d.ContainsKey(key);
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
            if (largo <= ancho) lineaActual += (string.IsNullOrEmpty(lineaActual) ? "" : " ") + palabra;
            else { printer.Text(lineaActual + "\n"); lineaActual = palabra; }
        }
        if (!string.IsNullOrEmpty(lineaActual)) printer.Text(lineaActual + "\n");
    }
}