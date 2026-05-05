using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Abstracción de transporte de impresora.
    /// Implementa este interfaz para añadir nuevos tipos de impresora
    /// (COM, USB, TCP/IP, Bluetooth, etc.) sin modificar PrinterService.
    /// </summary>
    public interface IPrinterTransport : IDisposable
    {
        /// <summary>
        /// Nombre descriptivo del transporte (ej: "COM3", "EPSON TM-T20II").
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Envía datos crudos a la impresora.
        /// </summary>
        Task SendAsync(byte[] data, CancellationToken ct);
    }
}
