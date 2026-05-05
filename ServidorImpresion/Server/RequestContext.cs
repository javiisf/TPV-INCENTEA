using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Contexto compartido entre handlers para una petición HTTP.
    /// Encapsula datos comunes extraídos del HttpListenerContext.
    /// </summary>
    public sealed class RequestContext
    {
        public HttpListenerContext HttpContext { get; }
        public string ClientIp { get; }
        public string RequestId { get; }
        public System.DateTime Timestamp { get; }
        public System.Diagnostics.Stopwatch Stopwatch { get; }
        public CancellationToken CancellationToken { get; }

        public HttpListenerRequest Request => HttpContext.Request;
        public HttpListenerResponse Response => HttpContext.Response;
        public string HttpMethod => Request.HttpMethod;
        public string Path => Request.Url?.AbsolutePath ?? "/";

        public long ContentLength => Request.ContentLength64;
        public string? UserAgent => Request.UserAgent;

        public RequestContext(HttpListenerContext httpContext, CancellationToken ct)
        {
            HttpContext = httpContext;
            ClientIp = httpContext.Request.RemoteEndPoint?.Address?.ToString() ?? "DESCONOCIDA";
            RequestId = System.Guid.NewGuid().ToString("N");
            Timestamp = System.DateTime.UtcNow;
            Stopwatch = System.Diagnostics.Stopwatch.StartNew();
            CancellationToken = ct;
        }

        /// <summary>
        /// Lee el body del request en bloques. Retorna (null, true) si se supera maxBytes.
        /// </summary>
        internal static async Task<(byte[]? Body, bool SizeExceeded)> TryReadBodyAsync(
            RequestContext ctx, long maxBytes)
        {
            if (ctx.ContentLength > 0 && ctx.ContentLength > maxBytes)
                return (null, true);

            using var ms = new MemoryStream();
            var buf = new byte[8192];
            int read; long total = 0;
            while ((read = await ctx.Request.InputStream.ReadAsync(buf, 0, buf.Length, ctx.CancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes) return (null, true);
                ms.Write(buf, 0, read);
            }
            return (ms.ToArray(), false);
        }
    }
}
