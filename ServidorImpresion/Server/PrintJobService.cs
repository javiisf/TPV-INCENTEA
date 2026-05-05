using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Resultado de un trabajo de impresión con contexto de backpressure.
    /// </summary>
    public readonly record struct PrintJobResult(bool IsSuccess, string? Message, bool RejectedByBackpressure = false);

    /// <summary>
    /// Petición interna encolada en el canal de impresión.
    /// </summary>
    internal sealed record PrintRequest(
        byte[] Payload,
        TaskCompletionSource<PrintJobResult> Completion,
        CancellationToken CancellationToken);

    /// <summary>
    /// Servicio de aplicación que desacopla la recepción HTTP del procesamiento de impresión
    /// mediante un canal productor-consumidor (System.Threading.Channels).
    ///
    /// Ventajas frente al antiguo SemaphoreSlim:
    ///   - Un único consumidor serializa los trabajos sin contención en _printLock.
    ///   - La cola actúa como búfer: más peticiones pueden esperar sin recibir 503 inmediato.
    ///   - DrainAsync permite vaciar trabajos pendientes antes de apagar el servidor.
    /// </summary>
    public sealed class PrintJobService : IDisposable
    {
        private readonly IPrinterService _printerService;
        private readonly Channel<PrintRequest> _queue;
        private readonly Action _onBackpressureRejected;
        private readonly Task _consumerTask;
        private readonly CancellationTokenSource _shutdownCts = new();
        private volatile int _pendingCount = 0;
        private int _disposed = 0;

        /// <summary>
        /// Número de trabajos actualmente en la cola (encolados pero aún no procesados).
        /// </summary>
        public int PendingCount => _pendingCount;

        public PrintJobService(
            IPrinterService printerService,
            int queueCapacity,
            Action onBackpressureRejected)
        {
            _printerService = printerService ?? throw new ArgumentNullException(nameof(printerService));
            _onBackpressureRejected = onBackpressureRejected ?? throw new ArgumentNullException(nameof(onBackpressureRejected));

            // Canal acotado: TryWrite devuelve false cuando está lleno (→ 503 inmediato)
            _queue = Channel.CreateBounded<PrintRequest>(new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // TryWrite falla si lleno; WriteAsync esperaría
                SingleReader = true,
                SingleWriter = false,
                // Evita que el hilo HTTP que hace TryWrite acabe ejecutando
                // la continuación del consumidor, lo que bloquearía la siguiente lectura.
                AllowSynchronousContinuations = false
            });

            _consumerTask = Task.Run(ConsumeLoopAsync);

            Log.Information("PrintJobService: canal de impresión iniciado. Capacidad={Capacity}", queueCapacity);
        }

        /// <summary>
        /// Valida el payload, lo encola y espera a que el consumidor lo procese.
        /// Retorna 503 si la cola está llena.
        /// </summary>
        public async Task<PrintJobResult> ExecuteAsync(byte[] payload, CancellationToken ct)
        {
            if (payload.Length == 0)
                return new PrintJobResult(false, "Datos vacíos");

            if (!PayloadValidator.IsValidPrinterPayload(payload))
                return new PrintJobResult(false, "Payload inválido");

            var tcs = new TaskCompletionSource<PrintJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new PrintRequest(payload, tcs, ct);

            // Incrementar antes de encolar para que el consumidor nunca decremente por debajo de cero.
            Interlocked.Increment(ref _pendingCount);

            // TryWrite no bloquea: si el canal está lleno devuelve false → 503
            if (!_queue.Writer.TryWrite(request))
            {
                Interlocked.Decrement(ref _pendingCount);
                _onBackpressureRejected();
                Log.Debug("PrintJobService: cola llena, rechazando trabajo ({Pending} pendientes)", _pendingCount);
                return new PrintJobResult(false, "Servidor ocupado", RejectedByBackpressure: true);
            }

            // Si la petición HTTP se cancela antes de que el consumidor la procese,
            // completamos la TCS con cancelación para liberar el hilo HTTP.
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

            return await tcs.Task.ConfigureAwait(false);
        }

        private async Task ConsumeLoopAsync()
        {
            try
            {
                // CancellationToken.None: el consumidor se detiene únicamente cuando el canal
                // se cierra (Writer.TryComplete), no por cancelación del shutdown.
                // Esto garantiza que DrainAsync vacía todos los trabajos antes de parar.
                await foreach (var request in _queue.Reader.ReadAllAsync(CancellationToken.None))
                {
                    Interlocked.Decrement(ref _pendingCount);

                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        request.Completion.TrySetCanceled(request.CancellationToken);
                        continue;
                    }

                    // Token combinado: petición HTTP + shutdown forzado.
                    // Permite cancelar el PrintAsync en vuelo sin romper el bucle de drenado.
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        request.CancellationToken, _shutdownCts.Token);

                    try
                    {
                        var result = await _printerService.PrintAsync(request.Payload, linkedCts.Token).ConfigureAwait(false);
                        request.Completion.TrySetResult(new PrintJobResult(result.IsSuccess, result.Message));
                    }
                    catch (OperationCanceledException ex)
                    {
                        request.Completion.TrySetCanceled(ex.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "PrintJobService: excepción inesperada procesando trabajo");
                        request.Completion.TrySetResult(new PrintJobResult(false, ex.Message));
                    }
                }

                Log.Information("PrintJobService: consumidor finalizado tras drenar la cola");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PrintJobService: error fatal en el consumidor");
            }
        }

        /// <summary>
        /// Cierra el canal a nuevas peticiones y espera a que la cola se vacíe completamente.
        /// Solo cancela el token de shutdown como último recurso tras agotar el timeout.
        /// </summary>
        public async Task DrainAsync(TimeSpan timeout)
        {
            // 1. Cerrar el canal: ningún Producer puede encolar más trabajos.
            //    El consumidor procesará todo lo que haya y luego terminará solo.
            _queue.Writer.TryComplete();

            Log.Information("PrintJobService: drenando cola ({Pending} trabajos pendientes)...", _pendingCount);

            // 2. Esperar a que el consumidor vacíe la cola de forma natural.
            //    No se cancela _shutdownCts aquí: el consumidor debe drenar hasta cero.
            using var timeoutCts = new CancellationTokenSource(timeout);
            try
            {
                await _consumerTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                Log.Information("PrintJobService: cola drenada correctamente");
            }
            catch (OperationCanceledException)
            {
                // 3. Solo si el timeout se agota cancela el token para abortar el PrintAsync en vuelo.
                Log.Warning(
                    "PrintJobService: timeout ({Timeout}s) esperando drenado. Trabajos sin procesar={Pending}. Forzando parada.",
                    timeout.TotalSeconds, _pendingCount);
                // Dispose() puede haberse llamado concurrentemente si StopAsync agotó su propio
                // timeout y el caller procedió al Dispose sin esperar a que DrainAsync terminara.
                try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { }
                try { await _consumerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.Writer.TryComplete();
            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
        }
    }
}
