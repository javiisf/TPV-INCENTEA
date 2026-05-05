using System;

namespace ServidorImpresion
{
    public class ConfigData
    {
        // Persistencia: Guardamos la última impresora y el puerto COM y el puerto del servidor para que el usuario no tenga que configurarlos cada vez que inicie la aplicación.
        public string UltimaUSB { get; set; } = "";
        public string UltimoCOM { get; set; } = "";
        public int PuertoServidor { get; set; } = 8080;

        // Puerto serie: velocidad (baudios). 9600 por defecto.
        public int BaudRate { get; set; } = 9600;

        // Seguridad: clave compartida para autorizar peticiones de impresión.
        // Si está vacía, el servidor acepta peticiones sin autenticación.
        public string ApiKey { get; set; } = "";

        // Límite de tamaño para tickets (bytes). 512KB por defecto para evitar DoS y derroche de papel.
        public int MaxTicketBytes { get; set; } = 512 * 1024;

        // Nivel de log: Debug, Information, Warning, Error. Configurable sin recompilar.
        public string NivelLog { get; set; } = "Information";

        // Estadísticas acumuladas a lo largo de múltiples sesiones.
        public long TrabajosAcumulados { get; set; } = 0;
        public long FallosAcumulados { get; set; } = 0;

        /// <summary>
        /// Corrige valores fuera de rango que podrían llegar de un JSON editado a mano.
        /// Se llama automáticamente tras deserializar desde disco.
        /// </summary>
        public void Sanitizar()
        {
            if (PuertoServidor <= 0 || PuertoServidor > 65535)
                PuertoServidor = 8080;

            // Baudios estándar: 300 – 115200
            if (BaudRate < 300 || BaudRate > 115200)
                BaudRate = 9600;

            // Límite de ticket: mínimo 1 KB, máximo 10 MB
            if (MaxTicketBytes < 1024 || MaxTicketBytes > 10 * 1024 * 1024)
                MaxTicketBytes = 512 * 1024;

            if (NivelLog != "Debug" && NivelLog != "Information" && NivelLog != "Warning" && NivelLog != "Error")
                NivelLog = "Information";

            // Acumulados nunca negativos
            if (TrabajosAcumulados < 0) TrabajosAcumulados = 0;
            if (FallosAcumulados < 0)   FallosAcumulados   = 0;
        }

        /// <summary>
        /// Indica si la configuración tiene los campos mínimos para operar
        /// (puerto válido + al menos un dispositivo seleccionado).
        /// </summary>
        public bool EsValida()
        {
            if (PuertoServidor <= 0 || PuertoServidor > 65535)
                return false;

            return !string.IsNullOrWhiteSpace(UltimoCOM) || !string.IsNullOrWhiteSpace(UltimaUSB);
        }

        /// <summary>
        /// Devuelve una copia superficial de esta configuración.
        /// Usar antes de mutar propiedades para no alterar el objeto que el servidor tiene en uso.
        /// </summary>
        public ConfigData Clone() => (ConfigData)MemberwiseClone();

        /// <summary>
        /// Aplica la selección de dispositivo según el nombre.
        /// Si empieza por "COM" se asigna a <see cref="UltimoCOM"/>, en caso contrario a <see cref="UltimaUSB"/>.
        /// </summary>
        public void AplicarDispositivo(string seleccion)
        {
            ArgumentException.ThrowIfNullOrEmpty(seleccion);

            if (seleccion.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                UltimoCOM = seleccion;
                UltimaUSB = "";
            }
            else
            {
                UltimaUSB = seleccion;
                UltimoCOM = "";
            }
        }
    }
}