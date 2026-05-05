using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Bloquea peticiones cuyo header Host no corresponda a localhost.
    /// Mitiga ataques de DNS rebinding: aunque la petición llegue a 127.0.0.1,
    /// si el Host dice "evil.com" se rechaza con 400 antes de cualquier procesamiento.
    /// Debe ser el primer filtro del pipeline.
    /// </summary>
    public sealed class LocalHostFilter : IRequestFilter
    {
        private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "[::1]"
        };

        public bool AppliesTo(RequestContext ctx) => true;

        public async Task<bool> ExecuteAsync(RequestContext ctx)
        {
            string? host = ctx.Request.Headers["Host"];

            if (string.IsNullOrWhiteSpace(host) || !IsAllowedHost(host))
            {
                Log.Warning("LocalHostFilter: Host rechazado. Host={Host}, IP={ClientIp}",
                    host ?? "(vacío)", ctx.ClientIp);
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                byte[] body = Encoding.UTF8.GetBytes("Bad Request");
                await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length, ctx.CancellationToken);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extrae el hostname del header Host (descartando el puerto) y comprueba que sea localhost.
        /// Soporta los tres formatos válidos: "hostname", "hostname:port" y "[::1]:port" (IPv6).
        /// </summary>
        private static bool IsAllowedHost(string hostHeader)
        {
            ReadOnlySpan<char> span = hostHeader.AsSpan().Trim();

            // IPv6: empieza con '[', p.e. "[::1]" o "[::1]:8080"
            if (span.StartsWith("["))
            {
                int closingBracket = span.IndexOf(']');
                if (closingBracket < 0) return false;
                string ipv6 = new string(span[..(closingBracket + 1)]);
                return AllowedHosts.Contains(ipv6);
            }

            // IPv4 o hostname: separar por ':' para quitar el puerto
            int colonIdx = span.IndexOf(':');
            string hostname = colonIdx >= 0
                ? new string(span[..colonIdx])
                : new string(span);

            return AllowedHosts.Contains(hostname);
        }
    }
}
