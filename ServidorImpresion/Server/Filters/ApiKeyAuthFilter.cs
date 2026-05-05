using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Filtro de autenticación por API key.
    /// Se aplica a todas las rutas, incluyendo /health.
    /// Acepta la clave desde el header X-Api-Key o el query param ?key=.
    /// La comparación usa tiempo constante para evitar timing attacks.
    /// Si no hay key configurada, permite todas las peticiones.
    /// </summary>
    public sealed class ApiKeyAuthFilter : IRequestFilter
    {
        private readonly Func<string> _apiKeyFactory;

        public ApiKeyAuthFilter(Func<string> apiKeyFactory)
        {
            _apiKeyFactory = apiKeyFactory ?? throw new ArgumentNullException(nameof(apiKeyFactory));
        }

        public bool AppliesTo(RequestContext ctx) => true;

        public async Task<bool> ExecuteAsync(RequestContext ctx)
        {
            string apiKey = _apiKeyFactory();

            if (string.IsNullOrWhiteSpace(apiKey))
                return true;

            // Header: todos los endpoints.
            // Query param ?key=: solo peticiones GET (abrir /health en el navegador).
            // Las peticiones POST (/print) deben usar siempre el header para evitar que
            // la clave aparezca en logs de proxies o en el historial del navegador.
            string? key = ctx.Request.Headers["X-Api-Key"];
            if (key is null && ctx.HttpMethod == "GET")
                key = ctx.Request.QueryString["key"];

            if (ConstantTimeEquals(key, apiKey))
                return true;

            Log.Warning("ApiKeyAuthFilter: api key inválida. IP={ClientIp}, Path={Path}, UserAgent={UserAgent}",
                ctx.ClientIp, ctx.Path, ctx.UserAgent ?? "(vacío)");
            ctx.Response.StatusCode = 401;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            byte[] body = Encoding.UTF8.GetBytes("Unauthorized");
            await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length, ctx.CancellationToken);
            return false;
        }

        /// <summary>
        /// Comparación en tiempo constante para evitar timing attacks.
        /// Compara los bytes UTF-8 de ambas cadenas sin cortocircuitar.
        /// </summary>
        private static bool ConstantTimeEquals(string? candidate, string expected)
        {
            if (candidate is null) return false;
            byte[] a = Encoding.UTF8.GetBytes(candidate);
            byte[] b = Encoding.UTF8.GetBytes(expected);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
