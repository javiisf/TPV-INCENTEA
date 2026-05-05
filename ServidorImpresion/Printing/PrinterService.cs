using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Servicio centralizado de impresión con sincronización thread-safe y logging.
    /// Delega la comunicación COM/USB a implementaciones de IPrinterTransport.
    /// </summary>
    public class PrinterService : IPrinterService
    {
        // Dos locks con ámbitos distintos:
        // - _printLock (SemaphoreSlim): mutex compatible con async que serializa los
        //   trabajos de impresión. Solo un trabajo corre a la vez porque el transporte
        //   es stateful y las impresoras procesan datos secuencialmente.
        // - _configLock (object): sincroniza lecturas/escrituras de config que son
        //   rápidas y no necesitan await. Un lock plano evita el overhead de
        //   SemaphoreSlim en secciones críticas de sub-microsegundo.
        private readonly SemaphoreSlim _printLock = new SemaphoreSlim(1, 1);
        private readonly object _configLock = new object();
        private ConfigData _config;
        private readonly Encoding _encoding;
        private IPrinterTransport? _transport;
        private volatile string _transportConfigKey = string.Empty;

        private const int MaxRetries = 3;
        private const int ReconnectIntervalSeconds = 10;

        // ESC @ (inicializar impresora): resetea el estado interno sin imprimir nada
        // visible ni avanzar el papel. Es universalmente compatible en todas las
        // impresoras ESC/POS, lo que lo convierte en la sonda de liveness más segura.
        private static readonly byte[] ProbePayload = [0x1B, 0x40];

        // Threshold 8, cooldown 15 s, half-open probe cada 3 s
        private readonly CircuitBreaker _circuitBreaker = new(threshold: 8,
            cooldown: TimeSpan.FromSeconds(15),
            probeInterval: TimeSpan.FromSeconds(3));

        private long _lastErrorAtTicks = DateTime.MinValue.Ticks;
        private string _lastErrorMessage = string.Empty;

        private long _totalJobsProcessed = 0;
        private long _totalJobsFailed = 0;

        private readonly PrintHistoryStore? _history;

        private readonly CancellationTokenSource _reconnectCts = new();
        private readonly Task _reconnectTask;

        public PrinterService(ConfigData config, Encoding encoding, PrintHistoryStore? history = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _history = history;

            _reconnectTask = Task.Run(() => ReconnectLoopAsync(_reconnectCts.Token));

            Log.Information("PrinterService inicializado. Impresora activa: COM={COM}, USB={USB}",
                _config.UltimoCOM, _config.UltimaUSB);
        }

        public ConfiguredDevice GetConfiguredDevice()
        {
            lock (_configLock)
            {
                if (!string.IsNullOrWhiteSpace(_config.UltimoCOM))
                {
                    return new ConfiguredDevice
                    {
                        Tipo = "com",
                        Nombre = _config.UltimoCOM,
                        BaudRate = _config.BaudRate
                    };
                }

                if (!string.IsNullOrWhiteSpace(_config.UltimaUSB))
                {
                    return new ConfiguredDevice
                    {
                        Tipo = "usb",
                        Nombre = _config.UltimaUSB
                    };
                }

                return new ConfiguredDevice
                {
                    Tipo = "no_configurado",
                    Nombre = string.Empty
                };
            }
        }

        /// <summary>
        /// Envía datos a imprimir con sincronización y reintentos.
        /// </summary>
        public async Task<PrintResult> PrintAsync(byte[] data, CancellationToken ct = default)
        {
            if (data == null || data.Length == 0)
            {
                Log.Warning("PrintAsync: datos vacíos recibidos");
                return PrintResult.FailureResult("Datos vacíos");
            }

            int maxBytes;
            lock (_configLock) { maxBytes = _config.MaxTicketBytes; }

            if (data.Length > maxBytes)
            {
                Log.Warning("PrintAsync: tamaño de carga excedido. Recibido: {Size} bytes, Máximo: {Max} bytes",
                    data.Length, maxBytes);
                return PrintResult.FailureResult($"Carga demasiado grande: {data.Length} bytes (máx: {maxBytes})");
            }

            string? rejection = _circuitBreaker.TryAcquire();
            if (rejection is not null)
                return PrintResult.FailureResult(rejection);

            await _printLock.WaitAsync(ct);
            try
            {
                ConfigSnapshot snapshot;
                lock (_configLock)
                {
                    snapshot = ConfigSnapshot.From(_config);
                }

                // Recreación lazy del transporte: la clave codifica puerto + baudios como
                // string barato. Si difiere de la última clave usada, el transporte se
                // descarta y se reconstruye en el siguiente SendAsync. Esto evita mantener
                // _configLock durante la construcción del transporte (que puede bloquearse
                // en SerialPort.Open) y elimina un posible deadlock con _printLock.
                string desiredKey = GetTransportKey(snapshot);
                if (_transportConfigKey != desiredKey)
                {
                    _transport?.Dispose();
                    _transport = null;
                    _transportConfigKey = desiredKey;
                }

                if (string.IsNullOrEmpty(desiredKey))
                {
                    Log.Warning("PrintAsync: No hay impresora configurada");
                    return PrintResult.FailureResult("No hay impresora configurada");
                }

                return await PrintWithRetriesAsync(data, snapshot, ct);
            }
            catch (OperationCanceledException)
            {
                Log.Information("PrintAsync: operación cancelada");
                return PrintResult.FailureResult("Operación cancelada");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalJobsFailed);
                SetLastError(ex.Message);
                Log.Error(ex, "PrintAsync: error inesperado");
                return PrintResult.FailureResult(ex.Message);
            }
            finally
            {
                _printLock.Release();
            }
        }

        /// <summary>
        /// Imprime con reintentos, delegando el envío al IPrinterTransport actual.
        /// </summary>
        /// <remarks>
        /// Estrategia de reconexión de transporte COM:
        ///   - Primer fallo: NO se desecha el objeto transporte. ComPrinterTransport ya cierra
        ///     el puerto internamente al fallar, de modo que EnsureOpen() lo reabrirá en el
        ///     siguiente intento reutilizando el mismo objeto SerialPort.
        ///   - Segundo fallo y posteriores: se desecha y se recrea el transporte para garantizar
        ///     un estado limpio antes del último intento.
        /// </remarks>
        private async Task<PrintResult> PrintWithRetriesAsync(byte[] data, ConfigSnapshot cfg, CancellationToken ct)
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    _transport ??= CreateTransport(cfg);

                    await _transport!.SendAsync(data, ct);

                    Interlocked.Increment(ref _totalJobsProcessed);

                    bool wasOpen = _circuitBreaker.RecordSuccess();
                    if (wasOpen)
                        Log.Information("PrintAsync: circuit breaker cerrado (recuperación exitosa). Dispositivo={Device}",
                            _transport.DisplayName);

                    Log.Information("PrintAsync: éxito. Dispositivo={Device}, Bytes={Size}, Intento={Attempt}",
                        _transport.DisplayName, data.Length, attempt);

                    _history?.Record(success: true, bytes: data.Length, device: _transport.DisplayName, errorMessage: null);

                    return PrintResult.SuccessResult($"Imprimido en {_transport.DisplayName}: {data.Length} bytes");
                }
                catch (Exception ex)
                {
                    string deviceName = _transport?.DisplayName ?? "desconocido";
                    Log.Warning(ex, "PrintAsync: fallo en intento {Attempt}/{MaxRetries}. Dispositivo={Device}, Bytes={Size}",
                        attempt, MaxRetries, deviceName, data.Length);

                    SetLastError($"{deviceName}: {ex.Message}");

                    if (attempt == MaxRetries)
                    {
                        // RecordFailure se llama solo tras agotar todos los reintentos,
                        // no en cada intento individual. Esto evita que un único trabajo
                        // con errores transitorios abra prematuramente el circuit breaker
                        // y bloquee trabajos posteriores que podrían ser correctos.
                        _transport?.Dispose();
                        _transport = null;

                        Interlocked.Increment(ref _totalJobsFailed);
                        _circuitBreaker.RecordFailure(deviceName);

                        _history?.Record(success: false, bytes: data.Length, device: deviceName, errorMessage: ex.Message);

                        Log.Error("PrintAsync: todos los reintentos fallaron. Dispositivo={Device}", deviceName);
                        return PrintResult.FailureResult($"Error en {deviceName} después de {MaxRetries} intentos: {ex.Message}");
                    }

                    // Primer fallo: el transporte (COM) ya cerró el puerto internamente.
                    // A partir del segundo fallo desechamos y recreamos el objeto.
                    if (attempt >= 2)
                    {
                        _transport?.Dispose();
                        _transport = null;
                    }

                    // Backoff lineal (500 ms, 1 000 ms) da tiempo al driver de la impresora
                    // a liberar el bloqueo del puerto antes del siguiente intento.
                    await Task.Delay(500 * attempt, ct);
                }
            }

            return PrintResult.FailureResult("Error desconocido");
        }

        // ── Reconexión automática ────────────────────────────────────────────────

        /// <summary>
        /// Loop de fondo: cuando el circuit breaker está abierto envía una sonda
        /// cada <see cref="ReconnectIntervalSeconds"/> segundos para detectar
        /// que la impresora volvió a estar disponible sin esperar a un trabajo real.
        /// </summary>
        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            Log.Debug("PrinterService: bucle de reconexión automática iniciado");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ReconnectIntervalSeconds), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var (isOpen, _, _) = _circuitBreaker.GetSnapshot();
                if (!isOpen)
                    continue;

                Log.Debug("PrinterService.Reconexión: circuit breaker abierto, lanzando sonda...");
                await TryProbeAsync(ct);
            }

            Log.Debug("PrinterService: bucle de reconexión automática detenido");
        }

        /// <summary>
        /// Intenta enviar <see cref="ProbePayload"/> a la impresora para verificar
        /// que la conexión se ha restablecido.
        /// — Si tiene éxito: cierra el circuit breaker.
        /// — Si falla: desecha el transporte pero NO llama RecordFailure para no
        ///   reiniciar el cooldown de los trabajos reales.
        /// </summary>
        private async Task TryProbeAsync(CancellationToken ct)
        {
            ConfigSnapshot snapshot;
            lock (_configLock)
            {
                snapshot = ConfigSnapshot.From(_config);
            }

            string desiredKey = GetTransportKey(snapshot);
            if (string.IsNullOrEmpty(desiredKey))
                return;

            bool acquired = await _printLock.WaitAsync(TimeSpan.FromSeconds(2), ct);
            if (!acquired)
            {
                Log.Debug("PrinterService.Reconexión: trabajo en curso, posponiendo sonda");
                return;
            }

            try
            {
                if (_transportConfigKey != desiredKey)
                {
                    _transport?.Dispose();
                    _transport = null;
                    _transportConfigKey = desiredKey;
                }

                _transport ??= CreateTransport(snapshot);
                string deviceName = _transport.DisplayName;

                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(TimeSpan.FromSeconds(3));

                await _transport.SendAsync(ProbePayload, probeCts.Token);

                _circuitBreaker.RecordSuccess();
                Log.Information("PrinterService.Reconexión: impresora recuperada. Dispositivo={Device}", deviceName);
            }
            catch (OperationCanceledException)
            {
                // Timeout de la sonda o shutdown: no es un fallo de la impresora
            }
            catch (Exception ex)
            {
                string deviceName = _transport?.DisplayName ?? "desconocido";
                Log.Debug("PrinterService.Reconexión: sonda fallida. Dispositivo={Device}, Error={Error}",
                    deviceName, ex.Message);

                _transport?.Dispose();
                _transport = null;
                // No llamamos RecordFailure: el circuit breaker ya está abierto y
                // reiniciar el cooldown penalizaría a los trabajos reales.
            }
            finally
            {
                _printLock.Release();
            }
        }

        private static string GetTransportKey(in ConfigSnapshot cfg)
        {
            if (!string.IsNullOrEmpty(cfg.UltimoCOM))
                return $"COM:{cfg.UltimoCOM}:{cfg.BaudRate}";
            if (!string.IsNullOrEmpty(cfg.UltimaUSB))
                return $"USB:{cfg.UltimaUSB}";
            return string.Empty;
        }

        private IPrinterTransport CreateTransport(in ConfigSnapshot cfg)
        {
            if (!string.IsNullOrEmpty(cfg.UltimoCOM))
                return new ComPrinterTransport(cfg.UltimoCOM, _encoding, cfg.BaudRate);
            return new UsbPrinterTransport(cfg.UltimaUSB);
        }

        /// <summary>
        /// Actualiza la configuración de forma thread-safe.
        /// </summary>
        public void UpdateConfig(ConfigData newConfig)
        {
            lock (_configLock)
            {
                _config = newConfig ?? throw new ArgumentNullException(nameof(newConfig));
            }

            // El transporte se recrea automáticamente en PrintAsync cuando detecta
            // que la configuración cambió. No cerramos aquí para evitar race condition.

            Log.Information("UpdateConfig: configuración actualizada. COM={COM}, USB={USB}, BaudRate={BaudRate}",
                newConfig.UltimoCOM, newConfig.UltimaUSB, newConfig.BaudRate);
        }

        // Refuerzo de concurrencia: acceso thread-safe a la configuración
        public ConfigData GetConfigSafe()
        {
            lock (_configLock)
            {
                return _config;
            }
        }

        /// <summary>
        /// Retorna estadísticas de impresión.
        /// </summary>
        public (long Total, long Failed) GetStatistics()
        {
            return (Interlocked.Read(ref _totalJobsProcessed), Interlocked.Read(ref _totalJobsFailed));
        }

        public PrinterStatus GetStatus()
        {
            var (isOpen, failures, remaining) = _circuitBreaker.GetSnapshot();

            var lastErrTicks = Interlocked.Read(ref _lastErrorAtTicks);
            DateTime? lastErr = lastErrTicks <= DateTime.MinValue.Ticks
                ? null
                : new DateTime(lastErrTicks, DateTimeKind.Utc);

            string lastMsg;
            lock (_configLock)
            {
                lastMsg = _lastErrorMessage;
            }

            return new PrinterStatus
            {
                CircuitBreakerOpen = isOpen,
                ConsecutiveFailures = failures,
                CooldownRemainingSeconds = remaining,
                LastErrorUtc = lastErr,
                LastErrorMessage = lastMsg
            };
        }

        private void SetLastError(string message)
        {
            Interlocked.Exchange(ref _lastErrorAtTicks, DateTime.UtcNow.Ticks);
            lock (_configLock)
            {
                _lastErrorMessage = message ?? string.Empty;
            }
        }

        public void Dispose()
        {
            _reconnectCts.Cancel();
            try { _reconnectTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _reconnectCts.Dispose();

            // Adquirir _printLock antes de disponer _transport para no interrumpir
            // un SendAsync en vuelo. Timeout de 2 s: si hay un trabajo bloqueado en
            // hardware en este punto, la cancelación ya habrá disparado y el trabajo
            // saldrá solo; procedemos igualmente para no bloquear el shutdown.
            bool hasLock = false;
            try { hasLock = _printLock.Wait(TimeSpan.FromSeconds(2)); } catch { }
            try
            {
                _transport?.Dispose();
                _transport = null;
            }
            finally
            {
                if (hasLock)
                    try { _printLock.Release(); } catch { }
            }

            _printLock?.Dispose();
            Log.Information("PrinterService: desechado");
        }
    }
}
