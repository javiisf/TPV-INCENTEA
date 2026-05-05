using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Handler HTTP para POST /print/zpl — payload binario ZPL directo.
    /// Solo se encarga de leer el body y delegar a PrintJobService.
    /// </summary>
    public sealed class PrintEndpointHandler : IRequestHandler
    {
        private readonly PrintJobService _printJobService;
        private readonly Func<long> _maxBytesFactory;

        public PrintEndpointHandler(
            PrintJobService printJobService,
            Func<long> maxBytesFactory)
        {
            _printJobService = printJobService;
            _maxBytesFactory = maxBytesFactory;
        }

        public bool CanHandle(RequestContext ctx)
            => ctx.HttpMethod == "POST" && ctx.Path == "/print/zpl";

        public async Task HandleAsync(RequestContext ctx)
        {
            long maxBytes = _maxBytesFactory();

            var (payload, exceeded) = await RequestContext.TryReadBodyAsync(ctx, maxBytes);
            if (exceeded)
            {
                Log.Warning("PrintEndpointHandler: carga rechazada por tamaño. Size={Size}", ctx.ContentLength);
                await WriteResponseAsync(ctx, 413, "Carga demasiado grande");
                return;
            }

            // Delegar validación + backpressure + impresión al servicio
            var result = await _printJobService.ExecuteAsync(payload!, ctx.CancellationToken);

            if (result.IsSuccess)
            {
                await WriteResponseAsync(ctx, 200, "OK");
            }
            else if (result.RejectedByBackpressure)
            {
                Log.Warning("PrintEndpointHandler: backpressure activo");
                await WriteResponseAsync(ctx, 503, "Servidor ocupado");
            }
            else
            {
                // Distinguir error de validación (4xx) vs error de impresión (5xx)
                bool isValidationError = result.Message == "Datos vacíos" || result.Message == "Payload inválido";
                int statusCode = isValidationError ? 400 : 500;
                Log.Debug("PrintEndpointHandler: resultado negativo. Error={Error}", result.Message);
                await WriteResponseAsync(ctx, statusCode, result.Message ?? "Error");
            }
        }

        private static async Task WriteResponseAsync(RequestContext ctx, int statusCode, string body)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            byte[] resBytes = Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length, ctx.CancellationToken);
        }
    }
}
