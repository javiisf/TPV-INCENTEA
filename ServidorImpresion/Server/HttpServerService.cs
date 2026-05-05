using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Context;

namespace ServidorImpresion
{
    /// <summary>
    /// Servidor HTTP ligero basado en HttpListener.
    /// Gestiona el ciclo de vida del listener y el pipeline de filtros/handlers.
    /// La construcción del pipeline es responsabilidad del llamador (AppHost).
    /// </summary>
    public class HttpServerService : IHttpServerService
    {
        private HttpListener? _listener;
        private readonly IRequestFilter[] _filters;
        private readonly IRequestHandler[] _handlers;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private int _currentPort;
        private volatile bool _isRunning = false;
        private int _rejectedByBackpressure = 0;

        private const int PrintQueueCapacityConst = 50;

        public event EventHandler<HttpServerErrorEventArgs>? ServerError;
        public bool IsRunning => _isRunning;
        public int CurrentPort => _currentPort;
        public int PendingCount => PrintJobService.PendingCount;
        public int QueueCapacity => PrintQueueCapacityConst;
        public int RejectedByBackpressure => Interlocked.CompareExchange(ref _rejectedByBackpressure, 0, 0);
        public PerIpRateLimiter RateLimiter { get; }
        public PrintJobService PrintJobService { get; }

        /// <summary>
        /// Constructor principal. El caller construye filtros y handlers con acceso a las
        /// propiedades del servidor (RateLimiter, PrintJobService, CurrentPort…) via la factory.
        /// </summary>
        public HttpServerService(
            IPrinterService printerService,
            Encoding encoding,
            Func<HttpServerService, (IRequestFilter[] Filters, IRequestHandler[] Handlers)> pipelineFactory)
        {
            _ = printerService ?? throw new ArgumentNullException(nameof(printerService));
            _ = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _ = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));

            RateLimiter = new PerIpRateLimiter(perIpLimit: 50, globalLimit: 200, window: TimeSpan.FromSeconds(1));
            PrintJobService = new PrintJobService(
                printerService,
                PrintQueueCapacityConst,
                () => Interlocked.Increment(ref _rejectedByBackpressure));

            (_filters, _handlers) = pipelineFactory(this);
        }

        /// <summary>
        /// Overload para tests: inyecta filtros y handlers directamente.
        /// </summary>
        public HttpServerService(
            IPrinterService printerService,
            Encoding encoding,
            IRequestFilter[] filters,
            IRequestHandler[] handlers)
            : this(printerService, encoding, _ => (filters, handlers))
        {
        }

        public async Task StartAsync(int port, CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                Log.Warning("HttpServerService.StartAsync: servidor ya está corriendo");
                return;
            }

            _currentPort = port;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();

            _isRunning = true;
            Log.Information("HttpServerService: servidor iniciado en puerto {Port}", port);

            // LongRunning: hilo dedicado en lugar de ThreadPool, ya que el loop vive toda la app.
            _serverTask = Task.Factory.StartNew(
                () => RunServerLoopAsync(_cts.Token),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;

            // 1. Dejar de aceptar conexiones nuevas sin cancelar los tokens de las
            //    peticiones ya en vuelo. _listener.Stop() hace que GetContextAsync()
            //    lance HttpListenerException, lo que saca al loop del servidor limpiamente.
            //    Si se cancelara _cts aquí, los CancellationTokens enlazados de cada
            //    petición también se cancelarían, y PrintJobService descartaría los
            //    trabajos encolados en lugar de procesarlos (ver ConsumeLoopAsync).
            try
            {
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HttpServerService.StopAsync: error deteniendo listener");
            }

            try
            {
                // 2. Drenar la cola: los tokens de petición siguen vivos, por lo que el
                //    consumidor procesa los trabajos pendientes en lugar de descartarlos.
                await PrintJobService.DrainAsync(TimeSpan.FromSeconds(10));

                // 3. Ahora que la cola está vacía, cancelar el CTS para detener el loop
                //    del servidor (si aún no terminó por sí solo tras _listener.Stop()).
                _cts?.Cancel();

                if (_serverTask != null && !_serverTask.IsCompleted)
                {
                    var completed = await Task.WhenAny(_serverTask, Task.Delay(TimeSpan.FromSeconds(2)));
                    if (completed != _serverTask)
                        Log.Warning("HttpServerService.StopAsync: servidor no se detuvo en el tiempo límite");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HttpServerService.StopAsync: error deteniendo servidor");
            }

            try
            {
                _listener?.Close();
                _listener = null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HttpServerService.StopAsync: error cerrando listener");
            }
        }

        public async Task RestartAsync(int newPort)
        {
            if (newPort != _currentPort)
            {
                int oldPort = _currentPort;
                Log.Information("HttpServerService.RestartAsync: puerto cambió de {OldPort} a {NewPort}", oldPort, newPort);
                await StopAsync();
                await Task.Delay(100);
                try
                {
                    await StartAsync(newPort, CancellationToken.None);
                }
                catch
                {
                    Log.Warning("HttpServerService.RestartAsync: fallo en puerto {NewPort}, restaurando puerto {OldPort}", newPort, oldPort);
                    await StartAsync(oldPort, CancellationToken.None);
                    throw;
                }
            }
        }

        private async Task RunServerLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _listener!.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();

                        var perRequestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        perRequestCts.CancelAfter(TimeSpan.FromSeconds(15));

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessRequestAsync(context, perRequestCts.Token);
                            }
                            finally
                            {
                                perRequestCts.Dispose();
                            }
                        }, ct);
                    }
                    catch (HttpListenerException) when (ct.IsCancellationRequested || !(_listener?.IsListening ?? false))
                    {
                        Log.Information("HttpServerService: servidor detenido");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        Log.Information("HttpServerService: listener desechado");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "HttpServerService: error en GetContextAsync");
                        if (!(_listener?.IsListening ?? false)) break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Log.Error(ex, "HttpServerService: error fatal");
                    ServerError?.Invoke(this, new HttpServerErrorEventArgs { Message = ex.Message, Exception = ex });
                }
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// Pipeline de procesamiento: rate-limit → auth → primer handler que coincida.
        /// </summary>
        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            var ctx = new RequestContext(context, ct);

            try
            {
                using (LogContext.PushProperty("RequestId", ctx.RequestId))
                using (LogContext.PushProperty("ClientIp", ctx.ClientIp))
                using (LogContext.PushProperty("HttpMethod", ctx.HttpMethod))
                using (LogContext.PushProperty("Path", ctx.Path))
                using (LogContext.PushProperty("ContentLength", ctx.ContentLength))
                using (LogContext.PushProperty("UserAgent", ctx.UserAgent ?? string.Empty))
                {
                    foreach (var filter in _filters)
                    {
                        if (filter.AppliesTo(ctx) && !await filter.ExecuteAsync(ctx))
                            return;
                    }

                    foreach (var handler in _handlers)
                    {
                        if (handler.CanHandle(ctx))
                        {
                            await handler.HandleAsync(ctx);
                            return;
                        }
                    }

                    Log.Debug("HttpServerService: no handler para la ruta");
                    await WriteResponseAsync(ctx, 404, "Not Found");
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("HttpServerService: timeout. ElapsedMs={ElapsedMs}",
                    ctx.Stopwatch.ElapsedMilliseconds);
                try { await WriteResponseAsync(ctx, 504, "Timeout", CancellationToken.None); } catch { }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HttpServerService: error inesperado. ElapsedMs={ElapsedMs}",
                    ctx.Stopwatch.ElapsedMilliseconds);
                try { await WriteResponseAsync(ctx, 500, "Error interno", CancellationToken.None); } catch { }
            }
            finally
            {
                ctx.Stopwatch.Stop();

                int statusCode;
                try
                {
                    statusCode = context.Response?.StatusCode ?? 0;
                    if (statusCode <= 0)
                        statusCode = 500;
                }
                catch
                {
                    statusCode = 500;
                }

                Log.Information("HttpServerService: request completado. StatusCode={StatusCode}, ElapsedMs={ElapsedMs}",
                    statusCode, ctx.Stopwatch.ElapsedMilliseconds);

                try { context.Response?.Close(); } catch { }
            }
        }

        private static async Task WriteResponseAsync(RequestContext ctx, int statusCode, string body, CancellationToken? overrideCt = null)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            byte[] resBytes = Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length, overrideCt ?? ctx.CancellationToken);
        }

        public void Dispose()
        {
            _cts?.Dispose();
            _listener?.Stop();
            _listener?.Close();
            RateLimiter?.Dispose();
            PrintJobService?.Dispose();
            Log.Information("HttpServerService: desechado");
        }
    }
}
