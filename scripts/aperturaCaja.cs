using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class AperturaCajaScript : ITicketScript
{
    const int MW = 42; // Ancho estándar que estamos usando

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);

        // Imprimimos 2 copias: una para el cajón y otra para el empleado/contabilidad
        for (int i = 0; i < 2; i++)
        {
            // ── Cabecera ──────────────────────────────────────────────────────
            printer.SetJustification(Justify.Center);
            printer.SetTextSize(2, 2);
            // Usamos WordWrap para que nombres largos no se corten
            WordWrap(printer, (string)empresa.nombreEmpresa, MW / 2); 
            
            printer.SetTextSize(1, 1);
            printer.Feed();
            printer.Text("CIF: " + empresa.cif + "\n");
            WordWrap(printer, (string)empresa.direccion);
            printer.Text(empresa.codPostal + " " + empresa.provincia + "\n");
            printer.Separator('='); // Separador visual para dar orden

            // ── Datos de Apertura ─────────────────────────────────────────────
            printer.SetJustification(Justify.Left);
            printer.Feed();
            printer.SetTextSize(1, 1);
            printer.Text("MOVIMIENTO: APERTURA DE CAJA\n");
            printer.Text("USUARIO   : " + ticket.usuario + "\n");
            printer.Feed(1);

            // ── Importe (Destacado) ───────────────────────────────────────────
            printer.SetTextSize(2, 2);
            decimal cantidad = (decimal)ticket.cantidadApertura;
            // Alineamos el texto "APERTURA" y el monto para que ocupe el ancho doble
            string label = "FONDO:";
            string monto = cantidad.ToString("N2") + "€";
            printer.Text(label + monto.PadLeft((MW / 2) - label.Length) + "\n");
            
            printer.SetTextSize(1, 1);
            printer.Feed(2);

            // ── Pie y Firma ───────────────────────────────────────────────────
            printer.SetJustification(Justify.Center);
            printer.Text("__________________________\n");
            printer.Text("Firma del Responsable\n");
            printer.Feed(1);
            printer.SetJustification(Justify.Left);
            printer.Text("Fecha: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "\n");
            
            printer.Feed(4);
            printer.Cut(); // Corta cada copia por separado
        }

        printer.Pulse(); // Abre el cajón físico
        return printer.Close();
    }

    // Mantenemos WordWrap para consistencia con venta.cs
    static void WordWrap(Printer printer, string texto, int ancho = MW)
    {
        if (string.IsNullOrEmpty(texto)) return;
        var palabras = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string lineaActual = "";
        foreach (var palabra in palabras)
        {
            if (palabra.Length > ancho)
            {
                if (!string.IsNullOrEmpty(lineaActual)) printer.Text(lineaActual + "\n");
                printer.Text(palabra[..(ancho - 1)] + "-\n");
                lineaActual = palabra[(ancho - 1)..];
                continue;
            }
            if ((lineaActual.Length + palabra.Length + 1) <= ancho)
                lineaActual += (lineaActual == "" ? "" : " ") + palabra;
            else { printer.Text(lineaActual + "\n"); lineaActual = palabra; }
        }
        if (!lineaActual != "") printer.Text(lineaActual + "\n");
    }
}