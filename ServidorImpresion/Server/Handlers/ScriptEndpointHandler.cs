using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Text.RegularExpressions;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Handler para POST /print/pos/{nombre} — ejecuta el script {nombre}.cs de la carpeta scripts/.
    /// </summary>
    public sealed class ScriptEndpointHandler : IRequestHandler
    {
        private readonly PrintJobService _printJobService;
        private readonly ScriptEngine _engine;
        private readonly Encoding _encoding;
        private readonly Func<long> _maxBytesFactory;
        private readonly string _configFolder;

        public ScriptEndpointHandler(PrintJobService printJobService, ScriptEngine engine,
            Encoding encoding, Func<long> maxBytesFactory, string configFolder)
        {
            _printJobService = printJobService;
            _engine = engine;
            _encoding = encoding;
            _maxBytesFactory = maxBytesFactory;
            _configFolder = configFolder;
        }

        public bool CanHandle(RequestContext ctx)
            => ctx.HttpMethod == "POST" && ctx.Path.StartsWith("/print/pos/")
            || ctx.HttpMethod == "GET"  && ctx.Path == "/print/pos";

        public async Task HandleAsync(RequestContext ctx)
        {
            if (ctx.HttpMethod == "GET" && ctx.Path == "/print/pos")
            {
                await HandleListAsync(ctx);
                return;
            }

            string name = ctx.Path["/print/pos/".Length..].Trim('/');
            if (string.IsNullOrEmpty(name)) { await WriteAsync(ctx, 400, "Nombre de script vacío"); return; }

            long maxBytes = _maxBytesFactory();
            var (bodyBytes, exceeded) = await RequestContext.TryReadBodyAsync(ctx, maxBytes);
            if (exceeded) { await WriteAsync(ctx, 413, "Carga demasiado grande"); return; }

            string body = Encoding.UTF8.GetString(bodyBytes!);

            ITicketScript? script;
            try { script = _engine.Load(name); }
            catch (Exception ex)
            {
                Log.Warning(ex, "ScriptEndpointHandler: error compilando '{Name}'", name);
                await WriteAsync(ctx, 400, ex.Message);
                return;
            }

            if (script is null) { await WriteAsync(ctx, 404, $"Script no encontrado: {name}.cs"); return; }

            JsonElement empresaData, ticketData;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                // acepta tanto array [ { empresaData, ticketData } ] como objeto directo
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
                    throw new JsonException("El array JSON está vacío");
                var obj = root.ValueKind == JsonValueKind.Array ? root[0] : root;
                var (empresaKey, ticketKey) = LoadMapping(_configFolder);
                empresaData = ResolveProperty(obj, empresaKey, "empresaData");
                ticketData  = ResolveProperty(obj, ticketKey,  "ticketData");
            }
            catch (Exception ex)
            {
                Log.Warning("ScriptEndpointHandler: JSON inválido para '{Name}': {Msg}", name, ex.Message);
                await WriteAsync(ctx, 400, "JSON inválido: " + ex.Message);
                return;
            }

            byte[] bytes;
            try { bytes = script.Render(ToExpando(empresaData), ToExpando(ticketData), _encoding); }
            catch (Exception ex)
            {
                int line = ScriptLineNumber(ex);
                string lineInfo = line > 0 ? $" (línea {line})" : "";
                bool campoFaltante = ex.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException";
                int status = campoFaltante ? 400 : 500;
                string msg = campoFaltante
                    ? $"Campo no encontrado en el JSON{lineInfo}: {ex.Message}"
                    : $"Error en el script{lineInfo} ({ex.GetType().Name}): {ex.Message}";
                Log.Error(ex, "ScriptEndpointHandler: error ejecutando '{Name}'{LineInfo}", name, lineInfo);
                await WriteAsync(ctx, status, msg);
                return;
            }

            var result = await _printJobService.ExecuteAsync(bytes, ctx.CancellationToken);

            if (result.IsSuccess)
                await WriteAsync(ctx, 200, "OK");
            else if (result.RejectedByBackpressure)
                await WriteAsync(ctx, 503, "Servidor ocupado");
            else
                await WriteAsync(ctx, 500, result.Message ?? "Error");
        }

        private async Task HandleListAsync(RequestContext ctx)
        {
            var nombres = _engine.ListScripts();
            string json = "[" + string.Join(",", System.Linq.Enumerable.Select(nombres, n => $"\"{n}\"")) + "]";
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ctx.CancellationToken);
        }

        private static int ScriptLineNumber(Exception ex)
        {
            var st = new StackTrace(ex, fNeedFileInfo: true);
            foreach (var frame in st.GetFrames() ?? [])
            {
                int n = frame.GetFileLineNumber();
                if (n > 0) return n;
            }
            return 0;
        }

        public static (string empresaKey, string ticketKey) LoadMapping(string scriptsFolder)
        {
            string path = Path.Combine(scriptsFolder, "mapping.json");
            if (!File.Exists(path))
                return ("empresaData", "ticketData");
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                string ParseKey(string prop, string def) =>
                    root.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
                        ? (string.IsNullOrEmpty(val.GetString()) ? def : val.GetString()!)
                        : def;
                return (ParseKey("empresaKey", "empresaData"),
                        ParseKey("ticketKey",  "ticketData"));
            }
            catch (Exception ex)
            {
                Log.Warning("ScriptEndpointHandler: error leyendo mapping.json, usando claves por defecto. {Msg}", ex.Message);
                return ("empresaData", "ticketData");
            }
        }

        internal static JsonElement ResolveProperty(JsonElement obj, string key, string role)
        {
            if (obj.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.Object)
                return val.Clone();
            throw new KeyNotFoundException(
                $"No se encontró '{role}' en el JSON. Clave configurada: '{key}'");
        }

        private static dynamic ToExpando(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                IDictionary<string, object?> obj = new ExpandoObject();
                foreach (var prop in el.EnumerateObject())
                    obj[prop.Name] = ToExpando(prop.Value);
                return obj;
            }
            if (el.ValueKind == JsonValueKind.Array)
            {
                var list = new List<dynamic>();
                foreach (var item in el.EnumerateArray())
                    list.Add(ToExpando(item));
                return list;
            }
            if (el.ValueKind == JsonValueKind.String)  return el.GetString()!;
            if (el.ValueKind == JsonValueKind.Number)  return el.GetDecimal();
            if (el.ValueKind == JsonValueKind.True)    return true;
            if (el.ValueKind == JsonValueKind.False)   return false;
            return null!;
        }

        private static readonly Regex _missingFieldRx =
            new(@"does not contain a definition for '([^']+)'", RegexOptions.Compiled);

        private static string ExtractMissingField(string message)
        {
            var m = _missingFieldRx.Match(message);
            return m.Success ? m.Groups[1].Value : message;
        }

        private static async Task WriteAsync(RequestContext ctx, int status, string body)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ctx.CancellationToken);
        }
    }
}
