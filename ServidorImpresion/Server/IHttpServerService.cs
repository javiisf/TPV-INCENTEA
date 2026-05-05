using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Interfaz para el servicio de servidor HTTP.
    /// Permite desacoplar la lógica del servidor de la UI.
    /// </summary>
    public interface IHttpServerService : IDisposable
    {
        /// <summary>
        /// Inicia el servidor HTTP en el puerto especificado.
        /// </summary>
        Task StartAsync(int port, CancellationToken cancellationToken);

        /// <summary>
        /// Detiene el servidor HTTP.
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Reinicia el servidor en un nuevo puerto.
        /// </summary>
        Task RestartAsync(int newPort);

        /// <summary>
        /// Evento que se dispara cuando hay un error en el servidor.
        /// </summary>
        event EventHandler<HttpServerErrorEventArgs> ServerError;

        /// <summary>
        /// Indica si el servidor está escuchando.
        /// </summary>
        bool IsRunning { get; }
    }

    /// <summary>
    /// Argumentos para el evento de error del servidor.
    /// </summary>
    public class HttpServerErrorEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public Exception Exception { get; set; } = new Exception("Error desconocido");
    }
}
