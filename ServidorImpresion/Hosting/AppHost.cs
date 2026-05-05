using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Orquesta el ciclo de vida de los servicios core (impresión + servidor HTTP).
    /// La UI consume este host para arrancar/parar y para consultar estado.
    /// </summary>
    public sealed class AppHost : IDisposable
    {
        private readonly Encoding _encoding;
        private readonly Func<(string ApiKey, int MaxTicketBytes)> _securityOptions;
        private readonly Func<(long TrabajosAcumulados, long FallosAcumulados)> _accumulatedStats;
        private readonly string _historyFilePath;
        private readonly string _configFolder;

        private IPrinterService? _printerService;
        private IHttpServerService? _httpServerService;
        private PrintHistoryStore? _historyStore;

        public event EventHandler<HttpServerErrorEventArgs>? ServerError;

        public bool IsRunning => _httpServerService?.IsRunning == true;

        public AppHost(Encoding encoding,
            Func<(string ApiKey, int MaxTicketBytes)> securityOptions,
            Func<(long TrabajosAcumulados, long FallosAcumulados)> accumulatedStats,
            string configFolder)
        {
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _securityOptions = securityOptions ?? throw new ArgumentNullException(nameof(securityOptions));
            _accumulatedStats = accumulatedStats ?? throw new ArgumentNullException(nameof(accumulatedStats));
            _configFolder = configFolder ?? throw new ArgumentNullException(nameof(configFolder));
            _historyFilePath = System.IO.Path.Combine(configFolder, "print_history.jsonl");
        }

        public async Task StartAsync(ConfigData config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);

            _historyStore ??= new PrintHistoryStore(_historyFilePath);
            _printerService ??= new PrinterService(config, _encoding, _historyStore);
            if (_httpServerService is null)
            {
                _httpServerService = new HttpServerService(_printerService, _encoding, BuildPipeline);
                _httpServerService.ServerError += OnServerError;
            }

            await _httpServerService.StartAsync(config.PuertoServidor, cancellationToken);
        }

        public Task RestartAsync(int newPort)
        {
            if (_httpServerService is null)
                throw new InvalidOperationException("Servidor HTTP no inicializado");

            return _httpServerService.RestartAsync(newPort);
        }

        public void UpdateConfig(ConfigData config)
        {
            ArgumentNullException.ThrowIfNull(config);
            _printerService?.UpdateConfig(config);
        }

        public (long Total, long Failed) GetStatistics()
            => _printerService?.GetStatistics() ?? (0L, 0L);

        /// <summary>
        /// Devuelve true si la impresora está operativa (circuit breaker cerrado).
        /// </summary>
        public bool IsPrinterHealthy()
            => !(_printerService?.GetStatus().CircuitBreakerOpen ?? false);

        public async Task StopAsync(TimeSpan timeout)
        {
            if (_httpServerService is null)
                return;

            using var cts = new CancellationTokenSource(timeout);
            await _httpServerService.StopAsync().WaitAsync(cts.Token);
        }

        private (IRequestFilter[] Filters, IRequestHandler[] Handlers) BuildPipeline(HttpServerService svc)
        {
            var filters = new IRequestFilter[]
            {
                new LocalHostFilter(),
                new RateLimitFilter(svc.RateLimiter),
                new ApiKeyAuthFilter(() => _securityOptions().ApiKey)
            };

            string scriptsFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
            var scriptEngine = new ScriptEngine(scriptsFolder);

            var handlers = new IRequestHandler[]
            {
                new HealthEndpointHandler(
                    _printerService!,
                    svc.RateLimiter,
                    _historyStore,
                    () =>
                    {
                        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_configFolder);
                        return new HealthSnapshotBuilder.ServerInput(
                            Puerto: svc.CurrentPort,
                            Ejecutandose: svc.IsRunning,
                            TrabajosEnCurso: svc.PendingCount,
                            MaxTrabajos: svc.QueueCapacity,
                            RechazadasPorSaturacion: svc.RejectedByBackpressure,
                            TrabajosAcumulados: _accumulatedStats().TrabajosAcumulados,
                            FallosAcumulados: _accumulatedStats().FallosAcumulados,
                            EmpresaKey: eKey,
                            TicketKey: tKey);
                    }),
                new PrintEndpointHandler(
                    svc.PrintJobService,
                    () => Math.Clamp(_securityOptions().MaxTicketBytes, 8 * 1024, 10 * 1024 * 1024)),
                new ScriptEndpointHandler(
                    svc.PrintJobService,
                    scriptEngine,
                    _encoding,
                    () => Math.Clamp(_securityOptions().MaxTicketBytes, 8 * 1024, 10 * 1024 * 1024),
                    _configFolder)
            };

            return (filters, handlers);
        }

        private void OnServerError(object? sender, HttpServerErrorEventArgs e)
            => ServerError?.Invoke(this, e);

        public void Dispose()
        {
            try
            {
                if (_httpServerService != null)
                    _httpServerService.ServerError -= OnServerError;
            }
            catch { }

            try
            {
                _httpServerService?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AppHost.Dispose: error desechando HttpServerService");
            }

            try
            {
                _printerService?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AppHost.Dispose: error desechando PrinterService");
            }

            _httpServerService = null;
            _printerService = null;

            try { _historyStore?.Dispose(); } catch { }
            _historyStore = null;
        }
    }
}
