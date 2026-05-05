using System;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Construye el DTO tipado HealthResponse para el endpoint /health.
    /// Recopila información de impresora, servidor, impresión y rate limiter
    /// sin acoplar esa lógica a HttpServerService.
    /// </summary>
    public static class HealthSnapshotBuilder
    {
        private static readonly TimeZoneInfo _tz =
            TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

        // Caché del estado del dispositivo: la comprobación WMI/COM es costosa y el
        // estado de la impresora no cambia en intervalos de segundos.
        private static (bool listo, string motivo) _cachedDeviceStatus;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static string _cachedDeviceKey = string.Empty;
        private static readonly object _cacheLock = new();
        private const int DeviceStatusTtlSeconds = 5;

        /// <summary>
        /// Convierte un instante UTC a hora local (Europe/Madrid) y lo formatea con el
        /// desplazamiento UTC real en ese momento: "GMT+2" en verano, "GMT+1" en invierno.
        /// </summary>
        private static string FormatLocalTimestamp(DateTime utcTime)
        {
            var local = TimeZoneInfo.ConvertTime(utcTime, _tz);
            var offset = _tz.GetUtcOffset(utcTime);
            string sign = offset >= TimeSpan.Zero ? "+" : "-";
            return local.ToString("dd/MM/yyyy HH:mm:ss") + $" GMT{sign}{(int)Math.Abs(offset.TotalHours)}";
        }

        /// <summary>
        /// Parámetros del servidor HTTP necesarios para construir el snapshot.
        /// </summary>
        public readonly record struct ServerInput(
            int Puerto,
            bool Ejecutandose,
            int TrabajosEnCurso,
            int MaxTrabajos,
            long RechazadasPorSaturacion,
            long TrabajosAcumulados = 0,
            long FallosAcumulados = 0,
            string? EmpresaKey = null,
            string? TicketKey = null);

        /// <summary>
        /// Construye el HealthResponse tipado de forma asíncrona.
        /// La consulta WMI al estado de la impresora USB se realiza en el ThreadPool
        /// para no bloquear el hilo del servidor HTTP.
        /// </summary>
        public static async Task<HealthResponse> BuildAsync(
            IPrinterService printerService,
            PerIpRateLimiter rateLimiter,
            ServerInput serverInput,
            DateTime requestTimestamp,
            PrintHistoryStore? historyStore = null)
        {
            var (totalJobs, failedJobs) = printerService.GetStatistics();
            var printerStatus = printerService.GetStatus();
            var cfgDevice = printerService.GetConfiguredDevice();

            string dispositivo = cfgDevice.Nombre;
            string tipoDispositivo = cfgDevice.Tipo;

            var (dispositivoListo, motivoNoListo) = await CheckDeviceReadyAsync(tipoDispositivo, dispositivo);

            // baudRate solo aplica a COM; para USB/no_configurado se deja en null
            int? baudRate = string.Equals(tipoDispositivo, "com", StringComparison.OrdinalIgnoreCase)
                ? (cfgDevice.BaudRate > 0 ? cfgDevice.BaudRate : 9600)
                : null;

            var stats = rateLimiter.GetStats();

            string version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "desconocida";

            return new HealthResponse
            {
                Version = version,
                Estado = "saludable",
                MarcaDeTiempo = FormatLocalTimestamp(requestTimestamp),
                ImpresoraSeleccionada = new SelectedPrinterInfo
                {
                    Tipo = tipoDispositivo,
                    Nombre = dispositivo,
                    BaudRate = baudRate,
                    Lista = dispositivoListo,
                    Motivo = motivoNoListo
                },
                Servidor = new ServerHealthInfo
                {
                    Puerto = serverInput.Puerto,
                    Ejecutandose = serverInput.Ejecutandose,
                    TrabajosImpresionEnCurso = serverInput.TrabajosEnCurso,
                    MaxTrabajosImpresionEnCurso = serverInput.MaxTrabajos,
                    RechazadasPorSaturacion = serverInput.RechazadasPorSaturacion
                },
                Impresion = new PrintHealthInfo
                {
                    TrabajosTotales = totalJobs,
                    TrabajosFallidos = failedJobs,
                    TrabajosHistorico = serverInput.TrabajosAcumulados + totalJobs,
                    FallosHistorico = serverInput.FallosAcumulados + failedJobs,
                    CortacircuitosAbierto = printerStatus.CircuitBreakerOpen,
                    FallosConsecutivos = printerStatus.ConsecutiveFailures,
                    SegundosRestantesEnfrio = printerStatus.CooldownRemainingSeconds,
                    UltimoErrorUtc = printerStatus.LastErrorUtc?.ToString("O"),
                    UltimoMensajeError = printerStatus.LastErrorMessage
                },
                LimitadorTasa = new RateLimitInfo
                {
                    SolicitudesGlobalesPorSegundo = stats.GlobalCount,
                    IpsPrincipales = stats.PerIpCounts
                        .OrderByDescending(x => x.Value)
                        .Take(5)
                        .ToDictionary(x => x.Key, x => x.Value)
                },
                HistorialImpresion = MapHistory(historyStore),
                Scripts = new ScriptsInfo
                {
                    ClaveEmpresa = serverInput.EmpresaKey ?? "empresaData",
                    ClaveTicket  = serverInput.TicketKey  ?? "ticketData"
                }
            };
        }

        private static PrintHistoryDto[] MapHistory(PrintHistoryStore? store)
        {
            if (store is null) return [];
            var records = store.GetRecent(50);
            var result = new PrintHistoryDto[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                var r = records[i];
                result[i] = new PrintHistoryDto
                {
                    TimestampLocal = TimeZoneInfo.ConvertTime(r.TimestampUtc, _tz)
                                        .ToString("dd/MM/yyyy HH:mm:ss"),
                    TimestampUtcMs = new DateTimeOffset(r.TimestampUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                    Exito = r.Success,
                    Bytes = r.Bytes,
                    Dispositivo = r.Device,
                    MensajeError = r.ErrorMessage
                };
            }
            return result;
        }

        private static async Task<(bool listo, string motivo)> CheckDeviceReadyAsync(string tipo, string nombre)
        {
            string key = $"{tipo}:{nombre}";
            lock (_cacheLock)
            {
                if (key == _cachedDeviceKey && DateTime.UtcNow < _cacheExpiry)
                    return _cachedDeviceStatus;
            }
            var result = await DoCheckDeviceReadyAsync(tipo, nombre);
            lock (_cacheLock)
            {
                _cachedDeviceKey = key;
                _cachedDeviceStatus = result;
                _cacheExpiry = DateTime.UtcNow.AddSeconds(DeviceStatusTtlSeconds);
            }
            return result;
        }

        private static async Task<(bool listo, string motivo)> DoCheckDeviceReadyAsync(string tipo, string nombre)
        {
            try
            {
                if (tipo == "usb" && !string.IsNullOrWhiteSpace(nombre))
                {
                    // WMI puede bloquear varios cientos de ms: lo ejecutamos en el ThreadPool
                    var (ready, reason) = await PrinterStatusChecker.TryGetPrinterReadyAsync(nombre);
                    return (ready, ready ? string.Empty : reason);
                }

                if (tipo == "com" && !string.IsNullOrWhiteSpace(nombre))
                {
                    // SerialPort.GetPortNames() es rápido (registro de Windows), no necesita Task.Run
                    var ports = SerialPort.GetPortNames();
                    bool found = ports.Any(p => string.Equals(p, nombre, StringComparison.OrdinalIgnoreCase));
                    return (found, found ? string.Empty : "Puerto COM no encontrado");
                }

                return (false, "No hay impresora configurada");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
