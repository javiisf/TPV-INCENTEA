using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class VentaScript : ITicketScript
{
    const int MW = 42;
    const int QW = 4;
    const int PW = 9;
    const int TW = 7; 
    const int FW = 9;
    const int DW = MW - QW - PW - TW - FW; 

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);

        // ── Cabecera empresa ──────────────────────────────────────────────────
        printer.SetJustification(Justify.Center);
        printer.SetTextSize(2, 2);
        WordWrap(printer, (string)empresa.nombreEmpresa, MW / 2);
        printer.SetTextSize(1, 1);
        printer.Feed();
        WordWrap(printer, "CIF: " + empresa.cif);
        WordWrap(printer, (string)empresa.direccion);
        WordWrap(printer, empresa.codPostal + " " + empresa.provincia);
        printer.Feed(2);

        printer.SetJustification(Justify.Left);
        WordWrap(printer, "Ticket: " + ticket.ticket);
        printer.Feed();

        // ── Cabecera tabla artículos ──────────────────────────────────────────
        int EW = FW + TW;
        printer.Text("Precio".PadLeft(MW - EW) + "Importe".PadLeft(EW) + "\n");
        printer.SetUnderline(true);
        printer.Text(
            "Ud. ".PadRight(QW) +
            "Descripción".PadRight(DW) +
            "Unidad".PadLeft(PW) +
            "%Dto.".PadLeft(TW) +
            "(€)  ".PadLeft(FW) + "\n");
        printer.SetUnderline(false);

        // ── Artículos ─────────────────────────────────────────────────────────
        decimal base4 = 0, base10 = 0, base21 = 0;
        decimal iva4  = 0, iva10  = 0, iva21  = 0;

        int    descMaxW = MW - QW;
        string padNum   = new string(' ', QW + DW);

        foreach (var item in ticket.items)
        {
            decimal cant       = (decimal)item.cantidad;
            decimal pUnidad    = (decimal)item.precioUnidad;
            decimal pDesc      = (decimal)item.precioDescuento;
            decimal dto        = Opt(item, "dtoPropio");
            decimal ivaT       = Opt(item, "iva", 21);
            decimal totalLinea = Math.Round(cant * pDesc, 2);

            string rawDesc = (string)item.descripcion;
            string desc    = rawDesc.Length > descMaxW ? rawDesc[..(descMaxW - 1)] + "." : rawDesc;

            string dtoStr    = dto > 0 ? ("%" + (int)dto) : "";
            string dtoPadded = dtoStr.PadLeft(dtoStr.Length + (TW - dtoStr.Length) / 2).PadRight(TW);

            printer.Text(((int)cant).ToString().PadLeft(QW - 1) + " " + desc + "\n");
            printer.Text(padNum + pUnidad.ToString("N2").PadLeft(PW) + dtoPadded + totalLinea.ToString("N2").PadLeft(FW) + "\n");

            decimal factor    = 1 + ivaT / 100m;
            decimal baseLinea = Math.Round(totalLinea / factor, 2);
            decimal ivaLinea  = Math.Round(baseLinea * ivaT / 100m, 2);

            if (ivaT == 4)  { base4  += baseLinea; iva4  += ivaLinea; }
            if (ivaT == 10) { base10 += baseLinea; iva10 += ivaLinea; }
            if (ivaT == 21) { base21 += baseLinea; iva21 += ivaLinea; }
        }

        // ── Total y formas de pago ────────────────────────────────────────────
        decimal total = (decimal)ticket.total;
        printer.Separator('=');
        printer.Text(Monto("TOTAL", total));
        printer.Feed(1);

        decimal ef = Opt(ticket, "pagoEfectivo");
        decimal tj = Opt(ticket, "pagoTarjeta");
        decimal vl = Opt(ticket, "pagoVale");
        decimal gi = Opt(ticket, "pagoGiro");

        if (ef > 0) printer.Text(Monto("EFECTIVO",      ef));
        if (tj > 0) printer.Text(Monto("TARJETA",       tj));
        if (vl > 0) printer.Text(Monto("VALE",          vl));
        if (gi > 0) printer.Text(Monto("DOMICILIACIÓN", gi));

        decimal cambio = Math.Round(ef - total, 2);
        if (cambio > 0) printer.Text(Monto("CAMBIO", cambio));

        // ── Desglose IVA ─────────────────────────────────────────────────────
        const int IW = 6, BW = 14, CW = 11;

        printer.Feed(1);
        printer.Text("DETALLE (€)\n");
        printer.Text("IVA".PadRight(IW) + "BASE IMPONIBLE".PadRight(BW) + "CUOTA".PadLeft(CW) + "\n");

        if (iva4  > 0) printer.Text("4%".PadRight(IW)  + base4.ToString("N2").PadLeft(BW)  + iva4.ToString("N2").PadLeft(CW)  + "\n");
        if (iva10 > 0) printer.Text("10%".PadRight(IW) + base10.ToString("N2").PadLeft(BW) + iva10.ToString("N2").PadLeft(CW) + "\n");
        if (iva21 > 0) printer.Text("21%".PadRight(IW) + base21.ToString("N2").PadLeft(BW) + iva21.ToString("N2").PadLeft(CW) + "\n");

        int tiposIva = (iva4 > 0 ? 1 : 0) + (iva10 > 0 ? 1 : 0) + (iva21 > 0 ? 1 : 0);
        if (tiposIva > 1)
            printer.Text("TOTAL".PadRight(IW) + (base4 + base10 + base21).ToString("N2").PadLeft(BW) + (iva4 + iva10 + iva21).ToString("N2").PadLeft(CW) + "\n");


        printer.Feed(2);

        // ── Pie ───────────────────────────────────────────────────────────────
        WordWrap(printer, "Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
        printer.Feed();
        printer.SetJustification(Justify.Left);
        WordWrap(printer, (string)empresa.textoLegal);
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

    string Monto(string label, decimal v)
        => label.PadRight(MW - FW) + v.ToString("N2").PadLeft(FW) + "\n";

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