using System;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    public sealed class AppStatusMonitor : IDisposable
    {
        private readonly Func<(bool IsRunning, int Port)> _serverState;
        private readonly Func<(long Total, long Failed)> _stats;
        private readonly Func<bool>? _printerHealthy;
        private readonly int _intervalMs;

        private System.Threading.Timer? _timer;
        private volatile bool _disposed;

        public event EventHandler<AppStatusSnapshot>? Snapshot;

        public AppStatusMonitor(
            Func<(bool IsRunning, int Port)> serverState,
            Func<(long Total, long Failed)> stats,
            int intervalMs = 2000,
            Func<bool>? printerHealthy = null)
        {
            _serverState = serverState ?? throw new ArgumentNullException(nameof(serverState));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _printerHealthy = printerHealthy;
            _intervalMs = intervalMs > 100 ? intervalMs : 2000;
        }

        public void Start()
        {
            ThrowIfDisposed();
            _timer ??= new System.Threading.Timer(_ => Tick(), null, 0, _intervalMs);
        }

        public void Stop()
        {
            _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
        }

        private void Tick()
        {
            if (_disposed) return;

            try
            {
                var (isRunning, port) = _serverState();
                var (_, failed) = _stats();

                // Si se proporcionó un proveedor de salud de impresora, úsalo (circuit breaker state).
                // Si no, cae al comportamiento anterior: sin fallos = sano.
                bool healthy = _printerHealthy != null ? _printerHealthy() : failed == 0;

                var snap = new AppStatusSnapshot(
                    IsServerRunning: isRunning,
                    Port: port,
                    PrinterHealthy: healthy,
                    FailedPrintJobs: failed,
                    TimestampUtc: DateTime.UtcNow);

                Snapshot?.Invoke(this, snap);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "AppStatusMonitor.Tick: error inesperado");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AppStatusMonitor));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }

    public readonly record struct AppStatusSnapshot(
        bool IsServerRunning,
        int Port,
        bool PrinterHealthy,
        long FailedPrintJobs,
        DateTime TimestampUtc);
}
