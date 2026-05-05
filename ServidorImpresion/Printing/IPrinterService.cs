using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Interfaz para el servicio de impresión.
    /// Permite mockear e inyectar diferentes implementaciones.
    /// </summary>
    public interface IPrinterService : IDisposable
    {
        /// <summary>
        /// Envía datos a imprimir de forma asincrónica.
        /// </summary>
        Task<PrintResult> PrintAsync(byte[] data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Actualiza la configuración de impresión.
        /// </summary>
        void UpdateConfig(ConfigData newConfig);

        /// <summary>
        /// Obtiene estadísticas de impresión (total, fallidas).
        /// </summary>
        (long Total, long Failed) GetStatistics();

        /// <summary>
        /// Obtiene estado operativo para diagnóstico (circuit breaker, último error).
        /// </summary>
        PrinterStatus GetStatus();

        /// <summary>
        /// Obtiene el dispositivo configurado actualmente (COM/USB) para diagnóstico.
        /// </summary>
        ConfiguredDevice GetConfiguredDevice();
    }
}
