using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ServidorImpresion
{
    /// <summary>
    /// Rate limiter con ventana deslizante que limita por IP cliente.
    /// Protege contra DoS: máximo N peticiones por IP en una ventana de tiempo.
    /// También mantiene un límite global.
    /// </summary>
    public class PerIpRateLimiter : IDisposable
    {
        private readonly int _perIpLimit;
        private readonly int _globalLimit;
        private readonly TimeSpan _window;
        private readonly object _lock = new object();
        private readonly int _maxTrackedIps;

        private readonly Dictionary<string, Queue<DateTime>> _perIpRequestTimes = new();
        private readonly Queue<DateTime> _globalRequestTimes = new();
        private DateTime _lastCleanup = DateTime.UtcNow;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(10);

        public PerIpRateLimiter(int perIpLimit, int globalLimit, TimeSpan window, int maxTrackedIps = 10_000)
        {
            _perIpLimit = perIpLimit;
            _globalLimit = globalLimit;
            _window = window;
            _maxTrackedIps = maxTrackedIps;
        }

        /// <summary>
        /// Intenta adquirir un permiso para la IP especificada.
        /// Retorna true si se permite, false si se excede límite (IP o global).
        /// </summary>
        public bool TryAcquire(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                ipAddress = "UNKNOWN";

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var windowStart = now.Subtract(_window);

                // ✅ Limpieza periódica de IPs inactivas para evitar crecimiento ilimitado.
                // Los queues están ordenados (más antiguo al frente), por lo que desencolar
                // desde el frente es O(elementos caducados) con early-exit al primer elemento
                // reciente — evita el All() anterior que era O(n·m) con el lock tomado.
                if (now - _lastCleanup > CleanupInterval)
                {
                    var keysToRemove = new List<string>();
                    foreach (var kvp in _perIpRequestTimes)
                    {
                        var q = kvp.Value;
                        while (q.Count > 0 && q.Peek() < windowStart)
                            q.Dequeue();
                        if (q.Count == 0)
                            keysToRemove.Add(kvp.Key);
                    }
                    foreach (var key in keysToRemove)
                        _perIpRequestTimes.Remove(key);
                    _lastCleanup = now;
                }

                // ✅ Protección contra IP-flooding: si se excede el límite de IPs rastreadas, rechazar
                if (!_perIpRequestTimes.ContainsKey(ipAddress) && _perIpRequestTimes.Count >= _maxTrackedIps)
                {
                    return false;
                }

                // ✅ Limpiar peticiones globales fuera de ventana
                while (_globalRequestTimes.Count > 0 && _globalRequestTimes.Peek() < windowStart)
                {
                    _globalRequestTimes.Dequeue();
                }

                // ✅ Verificar límite global
                if (_globalRequestTimes.Count >= _globalLimit)
                {
                    return false;
                }

                // ✅ Limpiar peticiones por IP fuera de ventana
                if (!_perIpRequestTimes.ContainsKey(ipAddress))
                {
                    _perIpRequestTimes[ipAddress] = new Queue<DateTime>();
                }

                var ipQueue = _perIpRequestTimes[ipAddress];
                while (ipQueue.Count > 0 && ipQueue.Peek() < windowStart)
                {
                    ipQueue.Dequeue();
                }

                // ✅ Verificar límite por IP
                if (ipQueue.Count >= _perIpLimit)
                {
                    return false;
                }

                // ✅ Registrar petición
                ipQueue.Enqueue(now);
                _globalRequestTimes.Enqueue(now);

                return true;
            }
        }

        /// <summary>
        /// Obtiene estadísticas actuales del rate limiter.
        /// </summary>
        public (int GlobalCount, Dictionary<string, int> PerIpCounts) GetStats()
        {
            Dictionary<string, Queue<DateTime>> snapshot;
            Queue<DateTime> globalSnapshot;
            DateTime windowStart;

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                windowStart = now.Subtract(_window);
                snapshot = new Dictionary<string, Queue<DateTime>>(_perIpRequestTimes);
                globalSnapshot = new Queue<DateTime>(_globalRequestTimes);
            }

            var perIpStats = snapshot
                .Where(kvp => kvp.Value.Count > 0)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count(t => t >= windowStart));

            var globalCount = globalSnapshot.Count(t => t >= windowStart);

            return (globalCount, perIpStats);
        }

        /// <summary>
        /// Limpia todas las estadísticas.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _perIpRequestTimes.Clear();
                _globalRequestTimes.Clear();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _perIpRequestTimes.Clear();
                _globalRequestTimes.Clear();
            }
        }
    }
}
