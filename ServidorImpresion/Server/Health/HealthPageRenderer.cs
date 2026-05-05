using System.Linq;
using System.Net;

namespace ServidorImpresion
{
    /// <summary>
    /// Genera la página HTML del endpoint /health.
    /// El template base se cachea estáticamente; solo los valores dinámicos se interpolan por request.
    /// </summary>
    public static class HealthPageRenderer
    {
        private static readonly string TemplatePrefix;
        private static readonly string TemplateSuffix;

        static HealthPageRenderer()
        {
            TemplatePrefix = @"<!doctype html>
<html lang='es'>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1' />
  <meta name='referrer' content='no-referrer' />
  <title>ServidorImpresion - Health</title>
  <link rel='stylesheet' href='https://fonts.googleapis.com/css2?family=Libre+Barcode+128+Text&display=swap' />
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; background: #0b0f14; color: #e6edf3; }
    a { color: #93c5fd; }
    .grid { display: grid; grid-template-columns: repeat(3, minmax(0,1fr)); gap: 14px; }
    @media (max-width: 820px) { .grid { grid-template-columns: repeat(2, minmax(0,1fr)); } }
    @media (max-width: 560px) { .grid { grid-template-columns: 1fr; } }
    .card { background: #111827; border: 1px solid #1f2937; border-radius: 10px; padding: 14px; }
    h1 { font-size: 18px; margin: 0 0 4px 0; }
    .sub { opacity: .8; font-size: 12px; margin-bottom: 14px; }
    .k { opacity: .75; font-size: 11px; text-transform: uppercase; letter-spacing: .05em; }
    .v { font-size: 14px; margin-top: 2px; word-break: break-word; }
    .row { display:flex; justify-content: space-between; gap: 10px; align-items: center; }
    .badge { padding: 2px 8px; border-radius: 999px; font-size: 12px; border: 1px solid transparent; }
    .ok { background: rgba(34,197,94,.15); border-color: rgba(34,197,94,.35); color: #86efac; }
    .bad { background: rgba(239,68,68,.15); border-color: rgba(239,68,68,.35); color: #fca5a5; }
    code { background: #0b1220; padding: 2px 6px; border-radius: 6px; }
    .small { font-size: 12px; opacity: .85; }
    #liveDot { display:inline-block; width:6px; height:6px; border-radius:50%; background:#4ade80; margin-left:6px; vertical-align:middle; opacity:.25; transition:opacity .1s ease; }
    #liveDot.pulse { opacity:1; }
    .header-section { padding-bottom:20px; margin-bottom:20px; border-bottom:1px solid #1f2937; }
    .status-bar { display:inline-flex; align-items:center; gap:8px; padding:5px 14px; border-radius:999px; font-size:13px; font-weight:500; margin-top:12px; border:1px solid transparent; }
    .status-ok  { background:rgba(34,197,94,.15); border-color:rgba(34,197,94,.35); color:#86efac; }
    .status-bad { background:rgba(239,68,68,.15);  border-color:rgba(239,68,68,.35);  color:#fca5a5; }
    .card-ok  { border-left: 3px solid rgba(34,197,94,.6); }
    .card-bad { border-left: 3px solid rgba(239,68,68,.6); }
  </style>
</head>
<body>
  <div class='header-section'>
  <h1>ServidorImpresion - Health</h1>
";

            TemplateSuffix = @"
  <script>
    function fmtBytes(b) {
      return b < 1024 ? '< 1 KB' : (b / 1024).toFixed(1) + ' KB';
    }

    function relTime(ms) {
      var diff = Math.floor((Date.now() - ms) / 1000);
      if (diff < 60)  return 'hace ' + diff + ' s';
      if (diff < 3600) return 'hace ' + Math.floor(diff / 60) + ' min';
      return 'hace ' + Math.floor(diff / 3600) + ' h';
    }

    function q(id) { return document.getElementById(id); }
    function setText(id, v) {
      var el = q(id);
      if (!el) return;
      el.textContent = (v === null || v === undefined) ? '' : ('' + v);
    }

    function setCb(open) {
      var el = q('cbBadge');
      if (el) {
        el.textContent = open ? 'ABIERTO' : 'CERRADO';
        el.classList.remove('ok', 'bad');
        el.classList.add(open ? 'bad' : 'ok');
      }
      var card = q('cbCard');
      if (card) {
        card.classList.remove('card-ok', 'card-bad');
        card.classList.add(open ? 'card-bad' : 'card-ok');
      }
    }

    function mapIps(obj) {
      if (!obj) return '';
      var parts = [];
      for (var k in obj) {
        if (Object.prototype.hasOwnProperty.call(obj, k)) {
          parts.push(k + '=' + obj[k]);
        }
      }
      return parts.join(', ');
    }

    // Preserve ?key= from the current URL so the refresh works when API Key is active.
    var _urlKey = new URLSearchParams(window.location.search).get('key');
    var _healthUrl = '/health' + (_urlKey ? '?key=' + encodeURIComponent(_urlKey) : '');

    async function refresh() {
      try {
        var res = await fetch(_healthUrl, { headers: { 'Accept': 'application/json' }, cache: 'no-store' });
        if (!res.ok) return;
        var h = await res.json();

        setText('ver', h.version);
        setText('ts', h.marcaDeTiempo);

        setText('puerto', h.servidor && h.servidor.puerto);
        setText('enCurso', h.servidor && h.servidor.trabajosImpresionEnCurso);
        setText('maxEnCurso', h.servidor && h.servidor.maxTrabajosImpresionEnCurso);
        setText('rechazadas', h.servidor && h.servidor.rechazadasPorSaturacion);

        var tot = (h.impresion && h.impresion.trabajosTotales) || 0;
        var fail = (h.impresion && h.impresion.trabajosFallidos) || 0;
        setText('trabTot', tot);
        setText('trabFail', fail);
        setText('successRate', tot > 0 ? ((tot - fail) * 100 / tot).toFixed(1) + '%' : '—');
        setText('trabHist', h.impresion && h.impresion.trabajosHistorico);
        setText('failHist', h.impresion && h.impresion.fallosHistorico);
        setText('fallosCons', h.impresion && h.impresion.fallosConsecutivos);
        setText('enfrio', h.impresion && h.impresion.segundosRestantesEnfrio);
        setCb(!!(h.impresion && h.impresion.cortacircuitosAbierto));

        var errMsg = (h.impresion && h.impresion.ultimoMensajeError) || '';
        setText('ultimoError', errMsg);
        setText('ultimoErrorUtc', h.impresion && h.impresion.ultimoErrorUtc);
        var errCard = q('lastErrorCard');
        if (errCard) { errCard.style.display = errMsg ? '' : 'none'; errCard.style.gridColumn = '1/-1'; }

        setText('glob', h.limitadorTasa && h.limitadorTasa.solicitudesGlobalesPorSegundo);
        setText('topIps', mapIps(h.limitadorTasa && h.limitadorTasa.ipsPrincipales));

        setText('impTipo', h.impresoraSeleccionada && h.impresoraSeleccionada.tipo);
        setText('impNombre', h.impresoraSeleccionada && h.impresoraSeleccionada.nombre);
        var baud = h.impresoraSeleccionada ? h.impresoraSeleccionada.baudRate : null;
        var baudRow = q('impBaudRow');
        if (baudRow) baudRow.style.display = (baud !== null && baud !== undefined) ? '' : 'none';
        setText('impBaud', baud !== null && baud !== undefined ? baud : '');
        setPrinterBadge(!!(h.impresoraSeleccionada && h.impresoraSeleccionada.lista));
        var motivo = (h.impresoraSeleccionada && h.impresoraSeleccionada.motivo) || '';
        setText('impMotivo', motivo);
        var motivoRow = q('impMotivoRow');
        if (motivoRow) motivoRow.style.display = motivo ? '' : 'none';

        setText('scriptEmpresa', h.scripts && h.scripts.claveEmpresa);
        setText('scriptTicket',  h.scripts && h.scripts.claveTicket);

        updateHistory(h.historialImpresion || []);
        updateStatus(h);

        var dot = q('liveDot');
        if (dot) {
          dot.classList.add('pulse');
          setTimeout(function() { dot.classList.remove('pulse'); }, 200);
        }
      } catch (e) { }
    }

    function setPrinterBadge(lista) {
      var el = q('impListaBadge');
      if (!el) return;
      el.textContent = lista ? 'LISTA' : 'NO LISTA';
      el.classList.remove('ok', 'bad');
      el.classList.add(lista ? 'ok' : 'bad');
    }

    function updateStatus(h) {
      var cbOpen = !!(h.impresion && h.impresion.cortacircuitosAbierto);
      var lista  = !!(h.impresoraSeleccionada && h.impresoraSeleccionada.lista);
      var ok = !cbOpen && lista;
      var el = q('statusBar');
      if (el) {
        el.className = 'status-bar ' + (ok ? 'status-ok' : 'status-bad');
        el.textContent = ok ? '● Sistema operativo'
          : cbOpen ? '● Circuit breaker abierto'
          : '● Impresora no disponible';
      }
      var sc = q('sistemaCard');
      if (sc) {
        sc.classList.remove('card-ok', 'card-bad');
        sc.classList.add(ok ? 'card-ok' : 'card-bad');
      }
    }

    function esc(s) {
      return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/'/g,'&#39;');
    }

    function buildHistoryRow(r) {
      var ok = r.exito;
      var border = ok ? '#22c55e' : '#ef4444';
      var rowBg  = ok ? '' : 'background:rgba(239,68,68,.04);';
      var cls  = ok ? 'badge ok' : 'badge bad';
      var lbl  = ok ? 'OK' : 'Error';
      var err  = r.mensajeError ? esc(r.mensajeError) : '';
      var rel  = r.timestampUtcMs ? relTime(r.timestampUtcMs) : esc(r.timestampLocal || '');
      var abs  = esc(r.timestampLocal || '');
      return '<tr style=\'border-bottom:1px solid #1a2030;border-left:2px solid ' + border + ';' + rowBg + '\'>'
        + '<td style=\'padding:5px 8px;white-space:nowrap;\' title=\'' + abs + '\'>' + rel + '</td>'
        + '<td style=\'padding:5px 8px;\'>' + fmtBytes(r.bytes) + '</td>'
        + '<td style=\'padding:5px 8px;max-width:160px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\'>' + esc(r.dispositivo || '') + '</td>'
        + '<td style=\'padding:5px 8px;\'><span class=\'' + cls + '\'>' + lbl + '</span></td>'
        + '<td style=\'padding:5px 8px;opacity:.8;font-size:11px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\'>' + err + '</td>'
        + '</tr>';
    }

    function updateHistory(history) {
      var tbody = document.getElementById('historyTbody');
      var count = document.getElementById('historyCount');
      if (!tbody) return;
      if (!history || history.length === 0) {
        tbody.innerHTML = '<tr><td colspan=\'5\' style=\'padding:12px;text-align:center;opacity:.5;\'>Sin historial</td></tr>';
        if (count) count.textContent = '(0 entradas)';
        return;
      }
      if (count) count.textContent = '(' + history.length + ' entradas)';
      var rows = '';
      for (var i = 0; i < history.length; i++) rows += buildHistoryRow(history[i]);
      tbody.innerHTML = rows;
    }

    setTimeout(refresh, 250);
    setInterval(refresh, 1000);
  </script>
</body>
</html>";
        }

        private static string RenderHistoryCard(PrintHistoryDto[] history)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($@"<div id='historyCard' class='card' style='margin-top:14px;grid-column:1/-1;'>
  <div class='k' style='margin-bottom:8px;'>Historial de impresión <span id='historyCount' style='opacity:.5;font-size:10px;text-transform:none;letter-spacing:0;'>({history.Length} entradas)</span></div>
  <div style='max-height:260px;overflow-y:auto;overflow-x:auto;'>
    <table style='width:100%;border-collapse:collapse;font-size:12px;'>
      <thead style='position:sticky;top:0;background:#111827;z-index:1;'>
        <tr style='border-bottom:1px solid #1f2937;'>
          <th style='text-align:left;padding:4px 8px;opacity:.7;'>Hora</th>
          <th style='text-align:left;padding:4px 8px;opacity:.7;'>Bytes</th>
          <th style='text-align:left;padding:4px 8px;opacity:.7;'>Dispositivo</th>
          <th style='text-align:left;padding:4px 8px;opacity:.7;'>Estado</th>
          <th style='text-align:left;padding:4px 8px;opacity:.7;'>Error</th>
        </tr>
      </thead>
      <tbody id='historyTbody'>");

            if (history.Length == 0)
            {
                sb.Append("<tr><td colspan='5' style='padding:12px;text-align:center;opacity:.5;'>Sin historial</td></tr>");
            }
            else
            {
                foreach (var r in history)
                {
                    string borderColor = r.Exito ? "#22c55e" : "#ef4444";
                    string rowBg       = r.Exito ? "" : "background:rgba(239,68,68,.04);";
                    string badgeClass  = r.Exito ? "badge ok" : "badge bad";
                    string badgeText   = r.Exito ? "OK" : "Error";
                    string error       = !string.IsNullOrEmpty(r.MensajeError) ? WebUtility.HtmlEncode(r.MensajeError) : string.Empty;
                    string kbText      = r.Bytes < 1024 ? "< 1 KB" : $"{r.Bytes / 1024.0:F1} KB";
                    TimeSpan age = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(r.TimestampUtcMs);
                    string relText = age.TotalSeconds < 60   ? $"hace {(int)age.TotalSeconds} s"
                                   : age.TotalHours   < 1    ? $"hace {(int)age.TotalMinutes} min"
                                   : $"hace {(int)age.TotalHours} h";
                    sb.Append($@"<tr style='border-bottom:1px solid #1a2030;border-left:2px solid {borderColor};{rowBg}' data-utc='{r.TimestampUtcMs}' onmouseover=""this.style.background='rgba(255,255,255,.03)'"" onmouseout=""this.style.background='{(r.Exito ? "" : "rgba(239,68,68,.04)")}'"">
  <td style='padding:5px 8px;white-space:nowrap;' title='{WebUtility.HtmlEncode(r.TimestampLocal)}'>{relText}</td>
  <td style='padding:5px 8px;'>{kbText}</td>
  <td style='padding:5px 8px;max-width:160px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;'>{WebUtility.HtmlEncode(r.Dispositivo)}</td>
  <td style='padding:5px 8px;'><span class='{badgeClass}'>{badgeText}</span></td>
  <td style='padding:5px 8px;opacity:.8;font-size:11px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;'>{error}</td>
</tr>");
                }
            }

            sb.Append("</tbody></table></div></div>");
            return sb.ToString();
        }

        /// <summary>
        /// Renderiza la página HTML de health interpolando valores dinámicos en el template cacheado.
        /// </summary>
        public static string Render(HealthResponse health)
        {
            bool cbOpen = health.Impresion.CortacircuitosAbierto;
            string cbBadge = cbOpen ? "badge bad" : "badge ok";
            string cbText = cbOpen ? "ABIERTO" : "CERRADO";

            string lastErrorUtc = health.Impresion.UltimoErrorUtc ?? "";
            string lastErrorMessage = health.Impresion.UltimoMensajeError ?? "";

            string topIps = health.LimitadorTasa.IpsPrincipales.Count > 0
                ? string.Join(", ", health.LimitadorTasa.IpsPrincipales.Select(kvp => $"{kvp.Key}={kvp.Value}"))
                : string.Empty;

            bool allOk = !cbOpen && health.ImpresoraSeleccionada.Lista;
            string statusClass = allOk ? "status-ok" : "status-bad";
            string statusText  = allOk ? "● Sistema operativo"
                : cbOpen ? "● Circuit breaker abierto"
                : "● Impresora no disponible";

            long total = health.Impresion.TrabajosTotales;
            long failed = health.Impresion.TrabajosFallidos;
            string successRate = total > 0 ? $"{(total - failed) * 100.0 / total:F1}%" : "—";

            bool baudVisible = health.ImpresoraSeleccionada.BaudRate.HasValue;
            bool lastErrorVisible = !string.IsNullOrEmpty(lastErrorMessage);

            string dynamicBody = $@"  <div class='sub'>Versión: <code id='ver'>{WebUtility.HtmlEncode(health.Version)}</code> · Marca de tiempo: <code id='ts'>{WebUtility.HtmlEncode(health.MarcaDeTiempo)}</code><span id='liveDot' title='Actualizando en vivo'></span></div>
  <div id='statusBar' class='status-bar {statusClass}'>{statusText}</div>
  </div>

  <div class='grid'>

    <!-- Sistema: full-width card combining server port + printer -->
    <div class='card {(allOk ? "card-ok" : "card-bad")}' id='sistemaCard' style='grid-column:1/-1;'>
      <div class='k' style='margin-bottom:6px;'>Sistema</div>
      <div class='row'>
        <span>Puerto <code id='puerto'>{health.Servidor.Puerto}</code>
          · En curso: <code id='enCurso'>{health.Servidor.TrabajosImpresionEnCurso}</code>/<code id='maxEnCurso'>{health.Servidor.MaxTrabajosImpresionEnCurso}</code>
          · Rechazadas: <code id='rechazadas'>{health.Servidor.RechazadasPorSaturacion}</code></span>
        <span>
          <code id='impTipo'>{WebUtility.HtmlEncode(health.ImpresoraSeleccionada.Tipo)}</code>
          · <code id='impNombre'>{WebUtility.HtmlEncode(health.ImpresoraSeleccionada.Nombre)}</code>
          <span id='impBaudRow'{(!baudVisible ? " style='display:none'" : "")}> · <code id='impBaud'>{(baudVisible ? health.ImpresoraSeleccionada.BaudRate!.Value.ToString() : "")}</code> bd</span>
          · <span id='impListaBadge' class='badge {(health.ImpresoraSeleccionada.Lista ? "ok" : "bad")}'>{(health.ImpresoraSeleccionada.Lista ? "LISTA" : "NO LISTA")}</span>
        </span>
      </div>
      <div id='impMotivoRow' class='v small'{(string.IsNullOrEmpty(health.ImpresoraSeleccionada.Motivo) ? " style='display:none'" : "")}>Motivo: <span id='impMotivo'>{WebUtility.HtmlEncode(health.ImpresoraSeleccionada.Motivo)}</span></div>
    </div>

    <!-- Impresión stats (session + historic) -->
    <div class='card'>
      <div class='k'>Impresión</div>
      <div class='v'>Trabajos: <code id='trabTot'>{health.Impresion.TrabajosTotales}</code> · Fallidos: <code id='trabFail'>{health.Impresion.TrabajosFallidos}</code></div>
      <div class='v small' style='margin-top:4px;'>Tasa de éxito: <code id='successRate'>{successRate}</code></div>
      <div class='v small' style='margin-top:2px;opacity:.6;'>Histórico: <code id='trabHist'>{health.Impresion.TrabajosHistorico}</code> · Fallidos: <code id='failHist'>{health.Impresion.FallosHistorico}</code></div>
    </div>

    <!-- Circuit breaker (isolated) -->
    <div id='cbCard' class='card {(cbOpen ? "card-bad" : "card-ok")}'>
      <div class='row'>
        <div class='k'>Circuit breaker</div>
        <div id='cbBadge' class='badge {cbBadge}'>{cbText}</div>
      </div>
      <div class='v' style='margin-top:6px;'>Fallos consecutivos: <code id='fallosCons'>{health.Impresion.FallosConsecutivos}</code></div>
      <div class='v small'>Segundos restantes enfriamiento: <code id='enfrio'>{health.Impresion.SegundosRestantesEnfrio}</code></div>
    </div>

    <div style='display:flex;gap:14px;'>
      <div class='card' style='flex:1;'>
        <div class='k'>Rate limiter (1s)</div>
        <div class='v'>Global/s: <code id='glob'>{health.LimitadorTasa.SolicitudesGlobalesPorSegundo}</code></div>
        <div class='v small'>Top IPs: <span id='topIps'>{WebUtility.HtmlEncode(topIps)}</span></div>
      </div>
      <div class='card' style='flex:1;'>
        <div class='k'>Mapping JSON</div>
        <div class='v'>Empresa: <code id='scriptEmpresa'>{WebUtility.HtmlEncode(health.Scripts.ClaveEmpresa)}</code></div>
        <div class='v'>Ticket: <code id='scriptTicket'>{WebUtility.HtmlEncode(health.Scripts.ClaveTicket)}</code></div>
      </div>
    </div>

    <div id='lastErrorCard' class='card'{(!lastErrorVisible ? " style='display:none;grid-column:1/-1'" : " style='grid-column:1/-1'")}>
      <div class='k'>Último error</div>
      <div class='v' id='ultimoError'>{WebUtility.HtmlEncode(lastErrorMessage)}</div>
      <div class='v small'><code id='ultimoErrorUtc'>{WebUtility.HtmlEncode(lastErrorUtc)}</code></div>
    </div>
  </div>

  {RenderHistoryCard(health.HistorialImpresion)}";

            return string.Concat(TemplatePrefix, dynamicBody, TemplateSuffix);
        }
    }
}
