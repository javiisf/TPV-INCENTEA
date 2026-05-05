using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Historial persistente de trabajos de impresión.
    /// Mantiene un buffer en memoria para lecturas rápidas y persiste cada entrada
    /// en un fichero JSONL (una línea por trabajo) en AppData.
    /// Al arrancar carga las últimas <see cref="MaxEntries"/> entradas del fichero.
    /// Cuando el fichero supera el doble del límite se reescribe para evitar crecimiento ilimitado.
    /// </summary>
    public sealed class PrintHistoryStore : IDisposable
    {
        private readonly string _filePath;
        private readonly int _maxEntries;
        private readonly object _lock = new();
        private readonly object _fileLock = new();
        private readonly Queue<PrintHistoryRecord> _buffer;
        private int _appendsSinceLastTrim = 0;
        private StreamWriter? _fileWriter;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public PrintHistoryStore(string filePath, int maxEntries = 500)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _maxEntries = maxEntries > 0 ? maxEntries : 500;
            _buffer = new Queue<PrintHistoryRecord>(_maxEntries);
            LoadFromFile();
        }

        // ── API pública ──────────────────────────────────────────────────────────────

        public void Record(bool success, int bytes, string device, string? errorMessage)
        {
            var entry = new PrintHistoryRecord(DateTime.UtcNow, success, bytes, device ?? string.Empty, errorMessage);

            // Solo el estado en memoria requiere el lock; la I/O de disco va fuera
            // para que GetRecent() no bloquee mientras se escribe en el fichero.
            bool shouldTrim;
            lock (_lock)
            {
                _buffer.Enqueue(entry);
                if (_buffer.Count > _maxEntries)
                    _buffer.Dequeue();

                _appendsSinceLastTrim++;
                shouldTrim = _appendsSinceLastTrim >= 100;
                if (shouldTrim)
                    _appendsSinceLastTrim = 0;
            }

            AppendToFile(entry);
            if (shouldTrim)
                TrimFileIfNeeded();
        }

        public PrintHistoryRecord[] GetRecent(int count)
        {
            lock (_lock)
            {
                return _buffer.TakeLast(Math.Min(count, _buffer.Count)).Reverse().ToArray();
            }
        }

        // ── Persistencia ─────────────────────────────────────────────────────────────

        private void LoadFromFile()
        {
            if (!File.Exists(_filePath)) return;

            try
            {
                // Queue deslizante: mantiene solo las últimas _maxEntries entradas en memoria
                // independientemente del tamaño del fichero (File.ReadLines es lazy/streaming).
                int totalLines = 0;
                var window = new Queue<PrintHistoryRecord>(_maxEntries + 1);

                foreach (var line in File.ReadLines(_filePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    totalLines++;
                    try
                    {
                        var r = JsonSerializer.Deserialize<PrintHistoryRecord>(line, JsonOpts);
                        if (r is not null)
                        {
                            window.Enqueue(r);
                            if (window.Count > _maxEntries)
                                window.Dequeue();
                        }
                    }
                    catch { /* línea corrupta: ignorar */ }
                }

                // Si el fichero tenía más líneas de las permitidas, reescribirlo recortado
                if (totalLines > _maxEntries)
                    RewriteFile(window);

                foreach (var r in window)
                    _buffer.Enqueue(r);

                Log.Information("PrintHistoryStore: {Count} entradas cargadas desde {Path}", window.Count, _filePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PrintHistoryStore: error cargando historial desde {Path}", _filePath);
            }
        }

        private void AppendToFile(PrintHistoryRecord entry)
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureDirectory();
                    _fileWriter ??= new StreamWriter(_filePath, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
                    _fileWriter.WriteLine(JsonSerializer.Serialize(entry, JsonOpts));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PrintHistoryStore: error escribiendo entrada en {Path}", _filePath);
                lock (_fileLock) { CloseWriter(); }
            }
        }

        private void CloseWriter()
        {
            _fileWriter?.Dispose();
            _fileWriter = null;
        }

        private void TrimFileIfNeeded()
        {
            try
            {
                lock (_fileLock) { CloseWriter(); }
                if (!File.Exists(_filePath)) return;

                int totalLines = 0;
                var window = new Queue<string>(_maxEntries + 1);

                foreach (var line in File.ReadLines(_filePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    totalLines++;
                    window.Enqueue(line);
                    if (window.Count > _maxEntries)
                        window.Dequeue();
                }

                if (totalLines > _maxEntries * 2)
                {
                    var parsed = new List<PrintHistoryRecord>(window.Count);
                    foreach (var l in window)
                    {
                        try
                        {
                            var r = JsonSerializer.Deserialize<PrintHistoryRecord>(l, JsonOpts);
                            if (r is not null) parsed.Add(r);
                        }
                        catch { }
                    }
                    RewriteFile(parsed);
                    Log.Debug("PrintHistoryStore: fichero recortado a {Count} entradas", parsed.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PrintHistoryStore: error al recortar fichero");
            }
        }

        private void RewriteFile(IEnumerable<PrintHistoryRecord> records)
        {
            try
            {
                EnsureDirectory();
                string tempPath = _filePath + ".tmp";
                using (var writer = new StreamWriter(tempPath, append: false))
                {
                    foreach (var r in records)
                        writer.WriteLine(JsonSerializer.Serialize(r, JsonOpts));
                }
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PrintHistoryStore: error reescribiendo fichero");
            }
        }

        private void EnsureDirectory()
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public void Dispose()
        {
            lock (_fileLock) { CloseWriter(); }
        }
    }
}
