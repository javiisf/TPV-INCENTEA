using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class DevolucionScript : ITicketScript
{
    const int MW = 42;
    const int QW = 4;
    const int PW = 9;
    const int TW = 7;
    const int FW = 9;
    const int DW = MW - QW - PW - TW - FW;

    const decimal RE4 = 0.005m, RE10 = 0.014m, RE21 = 0.052m;

    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc, MW);
        var cliente = ticket.cliente;

        // ── IVA / RE previo ───────────────────────────────────────────────────
        bool tieneRE = OptStr(cliente, "regimen") == "REQ";
        decimal base4 = 0, base10 = 0, base21 = 0;
        decimal iva4  = 0, iva10  = 0, iva21  = 0;
        decimal reBase4 = 0, reBase10 = 0, reBase21 = 0;
        decimal re4   = 0, re10   = 0, re21   = 0;
        foreach (var item in ticket.items)
        {
            decimal totalLinea = Math.Round((decimal)item.cantidad * (decimal)item.precioDescuento, 2);
            decimal ivaT       = (decimal)item.iva;
            decimal reTipo     = tieneRE ? (ivaT == 4 ? RE4 : ivaT == 10 ? RE10 : ivaT == 21 ? RE21 : 0) : 0;
            decimal factor     = 1 + ivaT / 100m + reTipo;
            decimal baseLinea  = Math.Round(totalLinea / factor, 2);
            decimal ivaLinea   = Math.Round(baseLinea * ivaT / 100m, 2);
            decimal reLinea    = Math.Round(baseLinea * reTipo, 2);
            if (ivaT == 4)  { base4  += baseLinea; iva4  += ivaLinea; if (reTipo > 0) { reBase4  += baseLinea; re4  += reLinea; } }
            if (ivaT == 10) { base10 += baseLinea; iva10 += ivaLinea; if (reTipo > 0) { reBase10 += baseLinea; re10 += reLinea; } }
            if (ivaT == 21) { base21 += baseLinea; iva21 += ivaLinea; if (reTipo > 0) { reBase21 += baseLinea; re21 += reLinea; } }
        }

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
        printer.Text("Devolución: " + ticket.ticket + "\n");
        printer.Feed();

        // ── Datos cliente (omitir si cliente interno "TPV") ───────────────────
        if ((string)cliente.razon_social != "TPV")
        {
            printer.Text("-------- DATOS CLIENTE --------\n");
            printer.Feed();
            WordWrap(printer, "Nombre: " + (string)cliente.razon_social, MW);
            printer.Feed();
            WordWrap(printer, "Dirección: " + (string)cliente.direccion + " " + (string)cliente.cod_postal, MW);
            printer.Feed();
            printer.Text("NIF: " + cliente.nif + "\n");
            printer.Feed(2);
        }

        // ── Artículos devueltos ───────────────────────────────────────────────
        int EW = FW + TW;
        printer.Text("Precio".PadLeft(MW - EW) + "Importe".PadLeft(EW) + "\n");
        printer.SetUnderline(true);
        printer.Text(
            "Ud. ".PadRight(QW) +
            "Descripción".PadRight(DW) +
            "Unidad".PadLeft(PW) +
            "%Dto.".PadLeft(TW) +
            "Total".PadLeft(FW) + "\n");
        printer.SetUnderline(false);

        int    descMaxW = MW - QW;
        string padQW    = new string(' ', QW);
        string padNum   = new string(' ', QW + DW);

        foreach (var item in ticket.items)
        {
            decimal cant       = (decimal)item.cantidad;
            decimal precio     = (decimal)item.precio;
            decimal descuento  = Opt(item, "descuento");
            decimal totalLinea = Math.Round(cant * (decimal)item.precioDescuento, 2);

            string rawDesc   = (string)item.descripcion;
            var    descLines = SplitDesc(rawDesc, descMaxW);
            string desc1     = descLines[0];
            string? desc2    = null;
            if (descLines.Count > 1)
            {
                string rest = rawDesc.Length > desc1.Length ? rawDesc[(desc1.Length)..].TrimStart() : "";
                desc2 = rest.Length > descMaxW ? rest[..(descMaxW - 1)] + "." : rest;
            }

            string dtoStr    = descuento > 0 ? ("%" + (int)descuento) : "";
            string dtoPadded = dtoStr.PadLeft(dtoStr.Length + (TW - dtoStr.Length) / 2).PadRight(TW);

            printer.Text(((int)cant).ToString().PadLeft(QW - 1) + " " + desc1 + "\n");
            if (desc2 != null) printer.Text(padQW + desc2 + "\n");
            printer.Text(padNum + precio.ToString("N2").PadLeft(PW) + dtoPadded + totalLinea.ToString("N2").PadLeft(FW) + "\n");
        }

        // ── Desglose impuestos ────────────────────────────────────────────────
        const int IW = 6, BW = 14, CW = 11;
        printer.Separator('=');
        printer.Text("DETALLE (€)\n");
        printer.Text("IVA".PadRight(IW) + "BASE IMPONIBLE".PadRight(BW) + "CUOTA".PadLeft(CW) + "\n");

        if (iva4  > 0) printer.Text("4%".PadRight(IW)  + base4.ToString("N2").PadLeft(BW)  + iva4.ToString("N2").PadLeft(CW)  + "\n");
        if (iva10 > 0) printer.Text("10%".PadRight(IW) + base10.ToString("N2").PadLeft(BW) + iva10.ToString("N2").PadLeft(CW) + "\n");
        if (iva21 > 0) printer.Text("21%".PadRight(IW) + base21.ToString("N2").PadLeft(BW) + iva21.ToString("N2").PadLeft(CW) + "\n");

        int tiposIva = (iva4 > 0 ? 1 : 0) + (iva10 > 0 ? 1 : 0) + (iva21 > 0 ? 1 : 0);
        if (tiposIva > 1)
            printer.Text("TOTAL".PadRight(IW) + (base4 + base10 + base21).ToString("N2").PadLeft(BW) + (iva4 + iva10 + iva21).ToString("N2").PadLeft(CW) + "\n");

        bool hayRE = re4 > 0 || re10 > 0 || re21 > 0;
        if (hayRE)
        {
            printer.Feed(1);
            printer.Text("R.E.".PadRight(IW) + "BASE IMPONIBLE".PadRight(BW) + "CUOTA".PadLeft(CW) + "\n");
            if (re4  > 0) printer.Text(((RE4  * 100).ToString("N1") + "%").PadRight(IW) + reBase4.ToString("N2").PadLeft(BW)  + re4.ToString("N2").PadLeft(CW)  + "\n");
            if (re10 > 0) printer.Text(((RE10 * 100).ToString("N1") + "%").PadRight(IW) + reBase10.ToString("N2").PadLeft(BW) + re10.ToString("N2").PadLeft(CW) + "\n");
            if (re21 > 0) printer.Text(((RE21 * 100).ToString("N1") + "%").PadRight(IW) + reBase21.ToString("N2").PadLeft(BW) + re21.ToString("N2").PadLeft(CW) + "\n");
            int tiposRE = (re4 > 0 ? 1 : 0) + (re10 > 0 ? 1 : 0) + (re21 > 0 ? 1 : 0);
            if (tiposRE > 1)
                printer.Text("TOTAL".PadRight(IW) + (reBase4 + reBase10 + reBase21).ToString("N2").PadLeft(BW) + (re4 + re10 + re21).ToString("N2").PadLeft(CW) + "\n");
        }

        // ── Total a devolver ──────────────────────────────────────────────────
        printer.Separator('=');
        printer.SetTextSize(2, 2);
        printer.Text(("DEVOL.:" + ((decimal)ticket.total).ToString("N2") + " EUR").PadLeft(MW / 2) + "\n");
        printer.SetTextSize(1, 1);
        printer.Feed(2);

        // ── Pie ───────────────────────────────────────────────────────────────
        printer.Text("Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "\n");
        printer.Feed(2);
        WordWrap(printer, (string)empresa.textoLegal, MW);
        printer.Feed();
        printer.SetJustification(Justify.Center);
        printer.Text("Gracias por su visita\n");
        printer.Feed();
        printer.Cut();
        printer.Pulse();

        return printer.Close();
    }

    static decimal Opt(dynamic obj, string key, decimal def = 0)
    {
        var d = (IDictionary<string, object?>)obj;
        return d.TryGetValue(key, out var v) && v != null ? Convert.ToDecimal(v) : def;
    }

    static string OptStr(dynamic obj, string key, string def = "")
    {
        var d = (IDictionary<string, object?>)obj;
        return d.TryGetValue(key, out var v) && v is string s ? s : def;
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
