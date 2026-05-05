using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class PresupuestoScript : ITicketScript
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

        printer.SetBold(true);
        printer.Text("*** PRESUPUESTO ***\n");
        printer.SetBold(false);
        printer.Feed();

        printer.SetJustification(Justify.Left);
        printer.Text("Nº presupuesto: " + ticket.ticket + "\n");
        printer.Text("Fecha:          " + DateTime.Now.ToString("dd/MM/yyyy") + "\n");
        printer.Feed();

        // ── Cliente ───────────────────────────────────────────────────────────
        if (Has(ticket, "cliente"))
        {
            var c = ticket.cliente;
            printer.Text("Cliente:\n");
            WordWrap(printer, (string)c.razonSocial, MW);
            string nif = OptStr(c, "nif");
            if (nif != "") printer.Text("NIF: " + nif + "\n");
            string dir = OptStr(c, "direccion");
            if (dir != "") WordWrap(printer, dir, MW);
            string cp  = OptStr(c, "codPostal");
            string loc = OptStr(c, "localidad");
            if (cp != "" || loc != "") printer.Text((cp + " " + loc).Trim() + "\n");
            printer.Feed();
        }

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
        decimal reBase4 = 0, reBase10 = 0, reBase21 = 0;
        decimal re4     = 0, re10     = 0, re21     = 0;
        decimal reRate4 = 0, reRate10 = 0, reRate21 = 0;

        int    descMaxW = MW - QW;
        string padQW    = new string(' ', QW);
        string padNum   = new string(' ', QW + DW);

        foreach (var item in ticket.items)
        {
            decimal cant       = (decimal)item.cantidad;
            decimal pUnidad    = (decimal)item.precioUnidad;
            decimal pDesc      = (decimal)item.precioDescuento;
            decimal dto        = Opt(item, "dtoPropio");
            decimal ivaT       = Opt(item, "iva", 21);
            decimal totalLinea = Math.Round(cant * pDesc, 2);

            string rawDesc   = (string)item.descripcion;
            var    descLines = SplitDesc(rawDesc, descMaxW);
            string desc1     = descLines[0];
            string? desc2    = null;
            if (descLines.Count > 1)
            {
                string rest = rawDesc.Length > desc1.Length ? rawDesc[(desc1.Length)..].TrimStart() : "";
                desc2 = rest.Length > descMaxW ? rest[..(descMaxW - 1)] + "." : rest;
            }

            string dtoStr    = dto > 0 ? ("%" + (int)dto) : "";
            string dtoPadded = dtoStr.PadLeft(dtoStr.Length + (TW - dtoStr.Length) / 2).PadRight(TW);

            printer.Text(((int)cant).ToString().PadLeft(QW - 1) + " " + desc1 + "\n");
            if (desc2 != null) printer.Text(padQW + desc2 + "\n");
            printer.Text(padNum + pUnidad.ToString("N2").PadLeft(PW) + dtoPadded + totalLinea.ToString("N2").PadLeft(FW) + "\n");

            decimal factor    = 1 + ivaT / 100m;
            decimal baseLinea = Math.Round(totalLinea / factor, 2);
            decimal ivaLinea  = Math.Round(baseLinea * ivaT / 100m, 2);

            if (ivaT == 4)  { base4  += baseLinea; iva4  += ivaLinea; }
            if (ivaT == 10) { base10 += baseLinea; iva10 += ivaLinea; }
            if (ivaT == 21) { base21 += baseLinea; iva21 += ivaLinea; }

            decimal reT = Opt(item, "re");
            if (reT > 0)
            {
                decimal reCuota = Math.Round(baseLinea * reT / 100m, 2);
                if (ivaT == 4)  { reRate4  = reT; reBase4  += baseLinea; re4  += reCuota; }
                if (ivaT == 10) { reRate10 = reT; reBase10 += baseLinea; re10 += reCuota; }
                if (ivaT == 21) { reRate21 = reT; reBase21 += baseLinea; re21 += reCuota; }
            }
        }

        // ── Total ─────────────────────────────────────────────────────────────
        printer.Separator('=');
        printer.Text(Monto("TOTAL", (decimal)ticket.total));
        printer.Feed(1);

        // ── Desglose IVA ─────────────────────────────────────────────────────
        const int IW = 6, BW = 14, CW = 11;

        printer.Text("DETALLE (€)\n");
        printer.Text("IVA".PadRight(IW) + "BASE IMPONIBLE".PadRight(BW) + "CUOTA".PadLeft(CW) + "\n");

        if (iva4  > 0) printer.Text("4%".PadRight(IW)  + base4.ToString("N2").PadLeft(BW)  + iva4.ToString("N2").PadLeft(CW)  + "\n");
        if (iva10 > 0) printer.Text("10%".PadRight(IW) + base10.ToString("N2").PadLeft(BW) + iva10.ToString("N2").PadLeft(CW) + "\n");
        if (iva21 > 0) printer.Text("21%".PadRight(IW) + base21.ToString("N2").PadLeft(BW) + iva21.ToString("N2").PadLeft(CW) + "\n");

        int tiposIva = (iva4 > 0 ? 1 : 0) + (iva10 > 0 ? 1 : 0) + (iva21 > 0 ? 1 : 0);
        if (tiposIva > 1)
            printer.Text("TOTAL".PadRight(IW) + (base4 + base10 + base21).ToString("N2").PadLeft(BW) + (iva4 + iva10 + iva21).ToString("N2").PadLeft(CW) + "\n");

        // ── Desglose R.E. (opcional) ─────────────────────────────────────────
        bool hayRE = re4 > 0 || re10 > 0 || re21 > 0;
        if (hayRE)
        {
            printer.Feed(1);
            printer.Text("R.E.".PadRight(IW) + "BASE IMPONIBLE".PadRight(BW) + "CUOTA".PadLeft(CW) + "\n");
            if (re4  > 0) printer.Text((reRate4.ToString("N1")  + "%").PadRight(IW) + reBase4.ToString("N2").PadLeft(BW)  + re4.ToString("N2").PadLeft(CW)  + "\n");
            if (re10 > 0) printer.Text((reRate10.ToString("N1") + "%").PadRight(IW) + reBase10.ToString("N2").PadLeft(BW) + re10.ToString("N2").PadLeft(CW) + "\n");
            if (re21 > 0) printer.Text((reRate21.ToString("N1") + "%").PadRight(IW) + reBase21.ToString("N2").PadLeft(BW) + re21.ToString("N2").PadLeft(CW) + "\n");
            int tiposRE = (re4 > 0 ? 1 : 0) + (re10 > 0 ? 1 : 0) + (re21 > 0 ? 1 : 0);
            if (tiposRE > 1)
                printer.Text("TOTAL".PadRight(IW) + (reBase4 + reBase10 + reBase21).ToString("N2").PadLeft(BW) + (re4 + re10 + re21).ToString("N2").PadLeft(CW) + "\n");
        }

        printer.Feed(2);

        // ── Validez (opcional) ────────────────────────────────────────────────
        int validezDias = (int)Opt(ticket, "validezDias");
        if (validezDias > 0)
        {
            printer.SetJustification(Justify.Left);
            string fechaLimite = DateTime.Now.AddDays(validezDias).ToString("dd/MM/yyyy");
            printer.Text("Válido hasta: " + fechaLimite + "\n");
            printer.Feed();
        }

        // ── Condiciones (opcional) ────────────────────────────────────────────
        string condiciones = OptStr(ticket, "condiciones");
        if (condiciones != "")
        {
            printer.SetJustification(Justify.Left);
            printer.Text("Condiciones:\n");
            WordWrap(printer, condiciones, MW);
            printer.Feed();
        }

        // ── Pie ───────────────────────────────────────────────────────────────
        WordWrap(printer, "Fecha y hora: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"), MW);
        printer.Feed();
        printer.SetJustification(Justify.Left);
        WordWrap(printer, (string)empresa.textoLegal, MW);
        printer.Feed();
        printer.SetJustification(Justify.Center);
        printer.SetBarcodeHeight(80);
        printer.SetBarcodeWidth(2);
        printer.Barcode((string)ticket.ticket);
        printer.Feed();
        printer.Cut();

        return printer.Close();
    }

    string Monto(string label, decimal v)
        => label.PadRight(MW - FW) + v.ToString("N2").PadLeft(FW) + "\n";

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

    static bool Has(dynamic obj, string key)
        => ((IDictionary<string, object?>)obj).ContainsKey(key);

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
