using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Transporte de impresora vía USB usando la API de Windows Spooler (winspool).
    /// </summary>
    public class UsbPrinterTransport : IPrinterTransport
    {
        private readonly string _printerName;

        // Cache del estado WMI: evita llamar WMI en cada job cuando hay ráfagas.
        // TTL de 4 s — balance entre frescura y coste (~1-2 s por consulta WMI).
        // Campos volatile: accedidos desde Task.Run (thread pool), volatile garantiza
        // visibilidad entre hilos sin necesidad de lock en el single-consumer path.
        private static readonly TimeSpan _statusTtl = TimeSpan.FromSeconds(4);
        private long   _lastStatusCheckTicks = DateTime.MinValue.Ticks; // acceso vía Interlocked
        private volatile bool   _lastStatusReady;
        private volatile string _lastStatusReason = string.Empty;

        public string DisplayName => _printerName;

        public UsbPrinterTransport(string printerName)
        {
            _printerName = printerName ?? throw new ArgumentNullException(nameof(printerName));
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Las llamadas P/Invoke (OpenPrinter / WritePrinter) y WMI bloquean el hilo.
            // Task.Run las delega al ThreadPool para que el pipeline HTTP no quede bloqueado.
            // WaitAsync(ct) permite al caller cancelar aunque el thread pool siga en WMI/spooler:
            // no podemos interrumpir P/Invoke sincrónico, pero sí liberar al caller.
            var task = Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // Consulta WMI cacheada: si el último resultado tiene menos de _statusTtl,
                // se reutiliza sin volver a llamar WMI. En ráfagas de tickets consecutivos
                // esto elimina la latencia WMI (1-2 s) del hot path.
                // Si el estado era "no lista" y el TTL expiró, se re-consulta para detectar
                // recuperación. El Circuit Breaker cubre fallos que escapen al cache.
                var now = DateTime.UtcNow;
                if (now.Ticks - Interlocked.Read(ref _lastStatusCheckTicks) >= _statusTtl.Ticks)
                {
                    _lastStatusReady  = PrinterStatusChecker.TryGetPrinterReady(_printerName, out var r);
                    _lastStatusReason = r ?? string.Empty;
                    Interlocked.Exchange(ref _lastStatusCheckTicks, now.Ticks);
                }

                if (!_lastStatusReady)
                    throw new InvalidOperationException($"Impresora no lista: {_lastStatusReason}");

                if (!RawPrinterHelper.SendBytesToPrinter(_printerName, data))
                    throw new InvalidOperationException($"SendBytesToPrinter retornó false para {_printerName}");
            }, ct);

            try
            {
                await task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // El task sigue en background (WMI/P/Invoke no interrumpibles).
                // Observamos la excepción para evitar UnobservedTaskException.
                ObserveTask(task);
                ct.ThrowIfCancellationRequested();
                throw;
            }
        }

        private static void ObserveTask(Task task) =>
            task.ContinueWith(static t => _ = t.Exception,
                              TaskContinuationOptions.OnlyOnFaulted |
                              TaskContinuationOptions.ExecuteSynchronously);

        public void Dispose()
        {
            // USB no mantiene conexión persistente
        }
    }
}
