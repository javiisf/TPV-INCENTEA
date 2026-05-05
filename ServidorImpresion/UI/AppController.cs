using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Mediador entre la UI y los servicios de negocio.
    /// Gestiona el ciclo de vida de <see cref="AppHost"/> y <see cref="AppStatusMonitor"/>,
    /// y reenvía sus eventos a la UI mediante <see cref="StatusChanged"/> y <see cref="ServerError"/>.
    /// </summary>
    public sealed class AppController : IDisposable
    {
        private static readonly HttpClient _httpClient = new();

        private readonly Encoding _encoding;
        private readonly ConfigStore _configStore;

        private AppHost? _appHost;
        private AppStatusMonitor? _statusMonitor;
        private volatile ConfigData _config;

        public event EventHandler<AppStatusSnapshot>? StatusChanged;
        public event EventHandler<HttpServerErrorEventArgs>? ServerError;

        public bool IsRunning => _appHost?.IsRunning == true;
        public ConfigData Config => _config;

        public AppController(Encoding encoding, ConfigStore configStore, ConfigData initialConfig)
        {
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _config = initialConfig ?? throw new ArgumentNullException(nameof(initialConfig));
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_appHost is null)
            {
                _appHost = new AppHost(_encoding,
                    () => (_config.ApiKey, _config.MaxTicketBytes),
                    () => (_config.TrabajosAcumulados, _config.FallosAcumulados),
                    System.IO.Path.GetDirectoryName(_configStore.ConfigPath)!);
                _appHost.ServerError += OnServerError;
            }

            await _appHost.StartAsync(_config, ct);

            StartMonitor();
            Log.Information("AppController: servicios iniciados");
        }

        public async Task StopAsync()
        {
            Log.Information("AppController: iniciando cierre de recursos");

            try
            {
                _statusMonitor?.Dispose();
                _statusMonitor = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AppController: error deteniendo monitor de estado");
            }

            if (_appHost is not null)
            {
                try
                {
                    var (total, failed) = _appHost.GetStatistics();
                    Log.Information("AppController: estadísticas finales. Total={Total}, Fallidas={Failed}", total, failed);

                    // Acumular contadores de sesión en la configuración persistente
                    _config.TrabajosAcumulados += total;
                    _config.FallosAcumulados += failed;
                    _configStore.Save(_config);

                    await _appHost.StopAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("AppController: timeout deteniendo servidor HTTP");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "AppController: error deteniendo servidor HTTP");
                }

                try
                {
                    _appHost.Dispose();
                    _appHost = null;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "AppController: error desechando AppHost");
                }
            }
        }

        public async Task RestartPortAsync(int newPort)
        {
            if (_appHost is null)
                throw new InvalidOperationException("Servidor HTTP no inicializado");
            await _appHost.RestartAsync(newPort);
        }

        /// <summary>
        /// Persiste la configuración y la propaga al servicio de impresión en caliente.
        /// </summary>
        public void SaveAndApplyConfig(ConfigData config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _configStore.Save(_config);
            _appHost?.UpdateConfig(_config);
            LogManager.SetLevel(_config.NivelLog);
        }

        public (long Total, long Failed) GetStatistics()
            => _appHost?.GetStatistics() ?? (0L, 0L);

        /// <summary>
        /// Estadísticas totales: sesión actual + sesiones anteriores persistidas.
        /// </summary>
        internal async Task<(bool IsSuccess, int StatusCode, string Body)> PostTestPayloadAsync(
            byte[] payload, CancellationToken ct = default)
        {
            string url = $"http://localhost:{_config.PuertoServidor}/print/zpl";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (!string.IsNullOrEmpty(_config.ApiKey))
                request.Headers.Add("X-Api-Key", _config.ApiKey);
            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (response.IsSuccessStatusCode, (int)response.StatusCode, body);
        }

        public (long Total, long Failed) GetStatisticsLifetime()
        {
            var (sessionTotal, sessionFailed) = GetStatistics();
            return (_config.TrabajosAcumulados + sessionTotal,
                    _config.FallosAcumulados + sessionFailed);
        }

        private void StartMonitor()
        {
            _statusMonitor?.Dispose();
            _statusMonitor = new AppStatusMonitor(
                serverState: () => (_appHost?.IsRunning == true, _config.PuertoServidor),
                stats: () => _appHost?.GetStatistics() ?? (0L, 0L),
                intervalMs: 2000,
                printerHealthy: () => _appHost?.IsPrinterHealthy() ?? true);

            _statusMonitor.Snapshot += (_, snap) => StatusChanged?.Invoke(this, snap);
            _statusMonitor.Start();
        }

        private void OnServerError(object? sender, HttpServerErrorEventArgs e)
        {
            Log.Error(e.Exception, "AppController: error en servidor HTTP: {Message}", e.Message);
            ServerError?.Invoke(this, e);
        }

        public void Dispose()
        {
            _statusMonitor?.Dispose();

            if (_appHost is not null)
            {
                try { _appHost.ServerError -= OnServerError; } catch { }
                try { _appHost.Dispose(); } catch { }
                _appHost = null;
            }
        }
    }
}
