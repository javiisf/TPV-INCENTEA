using System;
using System.Collections.Generic;
using System.Text;
using ServidorImpresion;

public class PedidoScript : ITicketScript
{
    const int MW = 42;
    const int QW = 4;
    const int PW = 9;
    const int TW = 7;
    const int FW = 9;
    const int DW = MW - QW - PW - TW - FW;
    const int PL = MW - FW;

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
        printer.Text("*** PEDIDO ***\n");
        printer.SetBold(false);
        printer.Feed();

        printer.SetJustification(Justify.Left);
        string fecha        = OptStr(ticket, "fecha", DateTime.Now.ToString("dd/MM/yyyy"));
        string fechaEntrega = OptStr(ticket, "fechaEntrega");
        printer.Text("Nº pedido:     " + ticket.pedido + "\n");
        printer.Text("Fecha:         " + fecha + "\n");
        if (fechaEntrega != "") printer.Text("F. entrega:    " + fechaEntrega + "\n");
        printer.Feed();

        // ── Proveedor (opcional) ──────────────────────────────────────────────
        if (Has(ticket, "proveedor"))
        {
            var p = ticket.proveedor;
            printer.Text("-------- PROVEEDOR --------\n");
            WordWrap(printer, (string)p.nombre, MW);
            string nif = OptStr(p, "nif");
            string dir = OptStr(p, "direccion");
            string cp  = OptStr(p, "codPostal");
            if (nif != "") printer.Text("NIF: " + nif + "\n");
            if (dir != "") WordWrap(printer, dir, MW);
            if (cp  != "") printer.Text(cp  + "\n");
            printer.Feed();
        }

        // ── Cabecera tabla ────────────────────────────────────────────────────
        int EW = FW + TW;
        printer.Text("Precio".PadLeft(MW - EW) + "Importe".PadLeft(EW) + "\n");
        printer.SetUnderline(true);
        printer.Text(
            "Ud. ".PadRight(QW) +
            "Descripción".PadRight(DW) +
            "P.Unit.".PadLeft(PW) +
            "%Dto.".PadLeft(TW) +
            "Total".PadLeft(FW) + "\n");
        printer.SetUnderline(false);

        // ── Artículos ─────────────────────────────────────────────────────────
        decimal totalGeneral = 0;
        int    descMaxW      = MW - QW;
        string padQW         = new string(' ', QW);
        string padNum        = new string(' ', QW + DW);

        foreach (var item in ticket.items)
        {
            decimal cant    = (decimal)item.cantidad;
            decimal pUnidad = (decimal)item.precioUnidad;
            decimal pFinal  = Has(item, "precioDescuento") ? (decimal)item.precioDescuento : pUnidad;
            decimal dto     = Opt(item, "dtoPropio");
            decimal total   = Math.Round(cant * pFinal, 2);
            totalGeneral   += total;

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
            printer.Text(padNum + pUnidad.ToString("N2").PadLeft(PW) + dtoPadded + total.ToString("N2").PadLeft(FW) + "\n");

            string ref_ = OptStr(item, "referencia");
            if (ref_ != "") printer.Text("    (Ref: " + ref_ + ")\n");
        }

        // ── Total ─────────────────────────────────────────────────────────────
        printer.Separator('=');
        printer.Text("TOTAL:".PadRight(PL) + totalGeneral.ToString("N2").PadLeft(FW) + "\n");
        printer.Feed();
        printer.Text("* Precios sin IVA.\n");
        printer.Text("  No válido como factura.\n");
        printer.Feed();

        // ── Observaciones (opcional) ──────────────────────────────────────────
        string obs = OptStr(ticket, "observaciones");
        if (obs != "")
        {
            printer.Text("Observaciones:\n");
            WordWrap(printer, obs, MW);
            printer.Feed();
        }

        // ── Firma (opcional, activa por defecto) ──────────────────────────────
        bool firma = !Has(ticket, "firma") || (bool)ticket.firma;
        if (firma)
        {
            printer.Feed(2);
            printer.Text("Firma autorizante:\n");
            printer.Feed(3);
            printer.Text("___________________________\n");
            printer.Feed();
        }

        printer.Cut();
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
