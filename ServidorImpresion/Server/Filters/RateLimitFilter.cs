using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Filtro de rate-limiting por IP. Se aplica a todas las rutas excepto /health.
    /// </summary>
    public sealed class RateLimitFilter : IRequestFilter
    {
        private readonly PerIpRateLimiter _rateLimiter;

        public RateLimitFilter(PerIpRateLimiter rateLimiter)
        {
            _rateLimiter = rateLimiter;
        }

        public bool AppliesTo(RequestContext ctx)
            => ctx.Path != "/health";

        public async Task<bool> ExecuteAsync(RequestContext ctx)
        {
            if (_rateLimiter.TryAcquire(ctx.ClientIp))
                return true;

            Log.Warning("RateLimitFilter: rate limit excedido. IP={ClientIp}", ctx.ClientIp);
            ctx.Response.StatusCode = 429;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            byte[] body = Encoding.UTF8.GetBytes("Demasiadas peticiones");
            await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length, ctx.CancellationToken);
            return false;
        }
    }
}
