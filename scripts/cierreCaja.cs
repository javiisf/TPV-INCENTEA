using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class CierreScript : ITicketScript
{
    const int MW = 42;

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);

        for (int i = 0; i < 2; i++)
        {
            // ── Cabecera ──────────────────────────────────────────────────────
            printer.SetJustification(Justify.Center);
            printer.SetTextSize(2, 2);
            printer.Text(empresa.nombreEmpresa + "\n");
            printer.SetTextSize(1, 1);
            printer.Feed();
            printer.Text("CIF: " + empresa.cif + "\n");
            printer.Text(empresa.direccion + "\n");
            printer.Text(empresa.codPostal + " " + empresa.provincia + "\n");
            printer.Feed();

            printer.SetTextSize(2, 1);
            printer.Text("CIERRE DE CAJA\n");
            printer.SetTextSize(1, 1);
            printer.Feed();
            printer.SetJustification(Justify.Left);
            printer.Text("USUARIO: " + ticket.usuario + "\n");
            printer.Feed();

            // ── Ventas ────────────────────────────────────────────────────────
            printer.SetTextSize(2, 1);
            printer.Text("VENTAS\n");
            printer.SetTextSize(1, 1);
            printer.Text(Row("Ventas Efectivo:", Opt(ticket, "totalVentaEfectivo")));
            printer.Text(Row("Ventas Tarjeta:",  Opt(ticket, "totalVentaTarjeta")));
            printer.Text(Row("Ventas Vale:",      Opt(ticket, "totalVentaVale")));
            printer.Text(Row("Ventas Giro:",      Opt(ticket, "totalVentaGiro")));
            printer.Text(Row("Total Ventas:",     Opt(ticket, "totalVentas")));
            printer.Feed();

            // ── Abonos ────────────────────────────────────────────────────────
            printer.SetTextSize(2, 1);
            printer.Text("ABONOS\n");
            printer.SetTextSize(1, 1);
            printer.Text(Row("Abonos Efectivo:", Opt(ticket, "totalAbonoEfectivo")));
            printer.Text(Row("Abonos Tarjeta:",  Opt(ticket, "totalAbonoTarjeta")));
            printer.Text(Row("Abonos Vale:",      Opt(ticket, "totalAbonoVale")));
            printer.Text(Row("Total Abonos:",     Opt(ticket, "totalAbonos")));
            printer.Feed();

            // ── Entradas / salidas ────────────────────────────────────────────
            printer.SetTextSize(2, 1);
            printer.Text("ENTRADAS/SALIDAS CAJA\n");
            printer.SetTextSize(1, 1);
            printer.Text(Row("Entrada:", Opt(ticket, "entradaCaja")));
            printer.Text(Row("Salida:",  Opt(ticket, "retiradaCaja")));
            printer.Feed();

            // ── Arqueo de caja ────────────────────────────────────────────────
            printer.SetTextSize(2, 1);
            printer.Text("ARQUEO CAJA\n");
            printer.SetTextSize(1, 1);
            printer.Text("Tarjeta\n");
            printer.Text(Row("Cierre datafono:", Opt(ticket, "cierreTarjeta")));
            printer.Text(Row("Diferencial:",     Opt(ticket, "difTarjeta")));
            printer.Text("Efectivo\n");
            printer.Text(Row("Saldo Inicial:",   Opt(ticket, "saldoInicialCaja")));
            printer.Text(Row("Saldo Teórico:",   Opt(ticket, "saldoTeoricoCaja")));
            printer.Text(Row("Cierre:",          Opt(ticket, "cierreMetalico")));
            printer.Text(Row("Diferencial:",     Opt(ticket, "saldoFinal")));
            printer.Feed(3);

            // ── Pie ───────────────────────────────────────────────────────────
            printer.Text("FIRMADO\n");
            printer.Feed(3);
            printer.Text("Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "\n");
            printer.Feed(3);
            printer.Cut();
        }

        printer.Pulse();
        return printer.Close();
    }

    static string Row(string label, decimal value)
    {
        string price = value.ToString("N2") + " €";
        return label.PadRight(MW - price.Length) + price + "\n";
    }

    static decimal Opt(dynamic obj, string key, decimal def = 0)
    {
        var d = (IDictionary<string, object?>)obj;
        return d.TryGetValue(key, out var v) && v != null ? Convert.ToDecimal(v) : def;
    }

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
        if (lineaActual != "") printer.Text(lineaActual + "\n");
    }
}