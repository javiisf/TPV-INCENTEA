using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Handler para GET /health.
    /// Construye el snapshot de salud y responde en JSON o HTML según el header Accept.
    /// </summary>
    public sealed class HealthEndpointHandler : IRequestHandler
    {
        private readonly IPrinterService _printerService;
        private readonly PerIpRateLimiter _rateLimiter;
        private readonly PrintHistoryStore? _historyStore;
        private readonly Func<HealthSnapshotBuilder.ServerInput> _serverInputFactory;

        public HealthEndpointHandler(
            IPrinterService printerService,
            PerIpRateLimiter rateLimiter,
            PrintHistoryStore? historyStore,
            Func<HealthSnapshotBuilder.ServerInput> serverInputFactory)
        {
            _printerService = printerService;
            _rateLimiter = rateLimiter;
            _historyStore = historyStore;
            _serverInputFactory = serverInputFactory;
        }

        public bool CanHandle(RequestContext ctx)
            => ctx.HttpMethod == "GET" && ctx.Path == "/health";

        public async Task HandleAsync(RequestContext ctx)
        {
            var serverInput = _serverInputFactory();
            var healthObj = await HealthSnapshotBuilder.BuildAsync(_printerService, _rateLimiter, serverInput, ctx.Timestamp, _historyStore);

            bool wantsHtml = false;
            try
            {
                string? accept = ctx.Request.Headers["Accept"];
                wantsHtml = !string.IsNullOrWhiteSpace(accept)
                    && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            ctx.Response.StatusCode = 200;

            if (wantsHtml)
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                string html = HealthPageRenderer.Render(healthObj);
                byte[] resBytes = Encoding.UTF8.GetBytes(html);
                await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length, ctx.CancellationToken);
            }
            else
            {
                ctx.Response.ContentType = "application/json";
                var json = System.Text.Json.JsonSerializer.Serialize(healthObj);
                byte[] resBytes = Encoding.UTF8.GetBytes(json);
                await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length, ctx.CancellationToken);
            }
        }
    }
}
