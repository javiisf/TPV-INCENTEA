using System;
using System.Threading;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Circuit breaker thread-safe con estados Closed / Open / Half-Open.
    ///
    /// Closed    → fallos consecutivos por debajo del threshold. Todas las peticiones pasan.
    /// Open      → threshold superado y cooldown activo. Todas las peticiones se rechazan.
    /// Half-Open → cooldown expirado. Se deja pasar una sola petición de prueba cada
    ///             ProbeInterval; si tiene éxito se cierra el circuito; si falla se reinicia
    ///             el cooldown.
    /// </summary>
    public sealed class CircuitBreaker
    {
        private readonly int _threshold;
        private readonly TimeSpan _cooldown;
        private readonly TimeSpan _probeInterval;

        private int _consecutiveFailures = 0;
        private long _circuitOpenedAtTicks = DateTime.MinValue.Ticks;
        private long _lastProbeFiredTicks = DateTime.MinValue.Ticks;

        public CircuitBreaker(
            int threshold = 8,
            TimeSpan? cooldown = null,
            TimeSpan? probeInterval = null)
        {
            if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            _threshold = threshold;
            _cooldown = cooldown ?? TimeSpan.FromSeconds(15);
            _probeInterval = probeInterval ?? TimeSpan.FromSeconds(3);
        }

        /// <summary>
        /// Verifica si la petición puede pasar.
        /// Devuelve <c>null</c> si se permite el paso, o un mensaje de rechazo en caso contrario.
        /// </summary>
        public string? TryAcquire()
        {
            if (Volatile.Read(ref _consecutiveFailures) < _threshold)
                return null; // Closed: paso libre

            var openedAt = new DateTime(Interlocked.Read(ref _circuitOpenedAtTicks), DateTimeKind.Utc);
            var elapsed = DateTime.UtcNow - openedAt;

            if (elapsed < _cooldown)
            {
                // Open: cooldown activo, rechazar
                int remaining = (int)Math.Ceiling((_cooldown - elapsed).TotalSeconds);
                Log.Warning("CircuitBreaker: abierto. Fallos={Failures}, EsperarSegundos={Remaining}",
                    _consecutiveFailures, remaining);
                return $"Impresora no disponible temporalmente. Reintente en {remaining} s.";
            }

            // Half-Open: cooldown expirado, permitir una sola sonda cada _probeInterval
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastProbeTicks = Interlocked.Read(ref _lastProbeFiredTicks);

            if (TimeSpan.FromTicks(nowTicks - lastProbeTicks) < _probeInterval)
            {
                Log.Debug("CircuitBreaker: en prueba, rechazando petición concurrente");
                return "Impresora en recuperación. Reintente en breve.";
            }

            // CAS garantiza que solo un hilo gane el turno de sonda cuando llegan
            // peticiones concurrentes en estado half-open. El hilo cuyo CAS tiene éxito
            // continúa; el resto se rechaza hasta que la sonda resuelve. Sin esto, una
            // ráfaga de peticiones pasaría todas a la vez y saturaria una impresora
            // que puede estar aún recuperándose.
            if (Interlocked.CompareExchange(ref _lastProbeFiredTicks, nowTicks, lastProbeTicks) != lastProbeTicks)
            {
                Log.Debug("CircuitBreaker: otro hilo ya tomó el rol de sonda");
                return "Impresora en recuperación. Reintente en breve.";
            }

            Log.Information("CircuitBreaker: modo half-open, intentando recuperación");
            return null; // Allowed as probe
        }

        /// <summary>
        /// Registra un éxito y cierra el circuito si estaba abierto.
        /// Devuelve <c>true</c> si el circuito estaba abierto antes (útil para log externo).
        /// </summary>
        public bool RecordSuccess()
        {
            bool wasOpen = Interlocked.Read(ref _circuitOpenedAtTicks) != DateTime.MinValue.Ticks;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            if (wasOpen)
            {
                Interlocked.Exchange(ref _circuitOpenedAtTicks, DateTime.MinValue.Ticks);
                Interlocked.Exchange(ref _lastProbeFiredTicks, DateTime.MinValue.Ticks);
            }
            return  wasOpen;
        }

        /// <summary>
        /// Registra un fallo (solo llamar tras el último reintento fallido).
        /// Abre o extiende el cooldown del circuito si se supera el threshold.
        /// </summary>
        public void RecordFailure(string deviceName)
        {
            int failures = Interlocked.Increment(ref _consecutiveFailures);
            if (failures >= _threshold)
            {
                Interlocked.Exchange(ref _circuitOpenedAtTicks, DateTime.UtcNow.Ticks);
                Log.Warning("CircuitBreaker: abierto. Fallos={Failures}, Dispositivo={Device}",
                    failures, deviceName);
            }
        }

        /// <summary>
        /// Snapshot del estado para diagnóstico / health endpoint.
        /// </summary>
        public (bool IsOpen, int ConsecutiveFailures, int CooldownRemainingSeconds) GetSnapshot()
        {
            int failures = Volatile.Read(ref _consecutiveFailures);
            var openedAt = new DateTime(Interlocked.Read(ref _circuitOpenedAtTicks), DateTimeKind.Utc);

            bool tripped = failures >= _threshold && openedAt != DateTime.MinValue;
            if (!tripped)
                return (false, failures, 0);

            var elapsed = DateTime.UtcNow - openedAt;
            int remaining = elapsed < _cooldown
                ? (int)Math.Max(0, Math.Ceiling((_cooldown - elapsed).TotalSeconds))
                : 0;

            return (true, failures, remaining);
        }
    }
}
