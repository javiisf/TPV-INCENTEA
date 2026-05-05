using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Transporte de impresora vía puerto serie (COM).
    /// Mantiene una conexión persistente al puerto y la reabre si cambia.
    /// </summary>
    public class ComPrinterTransport : IPrinterTransport
    {
        private readonly string _portName;
        private readonly Encoding _encoding;
        private readonly int _writeTimeoutMs;
        private readonly int _baudRate;
        private SerialPort? _serialPort;

        public string DisplayName => _portName;

        public ComPrinterTransport(string portName, Encoding encoding, int baudRate = 9600, int writeTimeoutMs = 10000)
        {
            _portName = portName ?? throw new ArgumentNullException(nameof(portName));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _baudRate = baudRate > 0 ? baudRate : 9600;
            _writeTimeoutMs = writeTimeoutMs;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            EnsureOpen();

            // Capturar la instancia local ANTES de crear el task para que si Dispose
            // pone _serialPort = null mientras el task está en vuelo, el task use el
            // objeto original (ya cerrado) y falle limpiamente en vez de lanzar NRE.
            var port = _serialPort;
            if (port == null || !port.IsOpen)
                throw new InvalidOperationException($"No se pudo abrir el puerto COM {_portName}");

            // SerialPort.Write no admite CancellationToken, así que se ejecuta en el ThreadPool
            // con CancellationToken.None. Para desbloquear el hilo ante timeout o cancelación
            // se cierra el puerto (TryClosePort), lo que hace que Write() lance una excepción
            // interna y libere el hilo rápidamente, en lugar de dejarlo bloqueado hasta que
            // el driver serie agote su propio WriteTimeout.
            var writeTask = Task.Run(() => port.Write(data, 0, data.Length), CancellationToken.None);

            // Token combinado: timeout propio + cancelación externa del caller.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_writeTimeoutMs);

            try
            {
                await writeTask.WaitAsync(timeoutCts.Token);
                return; // Éxito
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Solo ha expirado nuestro timeout interno; no es una cancelación del caller.
                // Cerramos el puerto para que port.Write() falle y libere el hilo del ThreadPool.
                TryClosePort();
                ObserveTask(writeTask);
                throw new TimeoutException($"Timeout ({_writeTimeoutMs} ms) escribiendo en {_portName}");
            }
            catch (OperationCanceledException)
            {
                // El caller canceló (ct). Misma mecánica: cerrar puerto para liberar el hilo.
                TryClosePort();
                ObserveTask(writeTask);
                ct.ThrowIfCancellationRequested();
                throw;
            }
            catch
            {
                // port.Write() lanzó una excepción (error hardware, puerto ya cerrado, etc.).
                // writeTask ya completó con fallo y fue observado por WaitAsync; solo cerramos.
                TryClosePort();
                throw;
            }
        }

        /// <summary>
        /// Suscribe una continuación que observa la excepción de una tarea para evitar
        /// <see cref="UnobservedTaskExceptionEventArgs"/> cuando la tarea termina en background
        /// tras un timeout o cancelación.
        /// </summary>
        private static void ObserveTask(Task task) =>
            task.ContinueWith(static t => _ = t.Exception,
                              TaskContinuationOptions.OnlyOnFaulted |
                              TaskContinuationOptions.ExecuteSynchronously);

        private void TryClosePort()
        {
            try { _serialPort?.Close(); } catch { }
        }

        private void EnsureOpen()
        {
            if (_serialPort == null)
            {
                _serialPort = new SerialPort(_portName, _baudRate)
                {
                    Encoding = _encoding,
                    // DTR y RTS deben activarse antes de abrir el puerto. La mayoría de
                    // impresoras ESC/POS serie usan estas líneas de control de flujo por
                    // hardware para señalizar disponibilidad; sin ellas la impresora puede
                    // descartar datos silenciosamente o no responder en absoluto.
                    DtrEnable = true,
                    RtsEnable = true,
                    WriteTimeout = _writeTimeoutMs
                };
                Log.Information("ComPrinterTransport: puerto serial creado. Puerto={Port}, BaudRate={BaudRate}", _portName, _baudRate);
            }

            if (!_serialPort.IsOpen)
            {
                _serialPort.Open();
                Log.Information("ComPrinterTransport: puerto serie abierto. Puerto={Port}", _portName);
            }
        }

        public void Dispose()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();
                    _serialPort.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ComPrinterTransport: error al cerrar puerto {Port}", _portName);
                }
                finally
                {
                    _serialPort = null;
                }
            }
        }
    }
}
