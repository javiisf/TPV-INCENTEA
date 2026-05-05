using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ServidorImpresion
{
    /// <summary>
    /// DTO tipado para la respuesta del endpoint /health.
    /// Elimina el uso de dynamic y objetos anónimos, mejorando seguridad de tipos
    /// y mantenibilidad.
    /// </summary>
    public sealed class HealthResponse
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("estado")]
        public string Estado { get; init; } = "saludable";

        [JsonPropertyName("marcaDeTiempo")]
        public string MarcaDeTiempo { get; init; } = string.Empty;

        [JsonPropertyName("impresoraSeleccionada")]
        public SelectedPrinterInfo ImpresoraSeleccionada { get; init; } = new();

        [JsonPropertyName("servidor")]
        public ServerHealthInfo Servidor { get; init; } = new();

        [JsonPropertyName("impresion")]
        public PrintHealthInfo Impresion { get; init; } = new();

        [JsonPropertyName("limitadorTasa")]
        public RateLimitInfo LimitadorTasa { get; init; } = new();

        [JsonPropertyName("historialImpresion")]
        public PrintHistoryDto[] HistorialImpresion { get; init; } = [];

        [JsonPropertyName("scripts")]
        public ScriptsInfo Scripts { get; init; } = new();
    }

    public sealed class ScriptsInfo
    {
        [JsonPropertyName("claveEmpresa")]
        public string ClaveEmpresa { get; init; } = "empresaData";

        [JsonPropertyName("claveTicket")]
        public string ClaveTicket { get; init; } = "ticketData";
    }

    public sealed class PrintHistoryDto
    {
        [JsonPropertyName("timestampLocal")]
        public string TimestampLocal { get; init; } = string.Empty;

        [JsonPropertyName("timestampUtcMs")]
        public long TimestampUtcMs { get; init; }

        [JsonPropertyName("exito")]
        public bool Exito { get; init; }

        [JsonPropertyName("bytes")]
        public int Bytes { get; init; }

        [JsonPropertyName("dispositivo")]
        public string Dispositivo { get; init; } = string.Empty;

        [JsonPropertyName("mensajeError")]
        public string? MensajeError { get; init; }
    }

    public sealed class SelectedPrinterInfo
    {
        [JsonPropertyName("tipo")]
        public string Tipo { get; init; } = "no_configurado";

        [JsonPropertyName("nombre")]
        public string Nombre { get; init; } = string.Empty;

        [JsonPropertyName("baudRate")]
        public int? BaudRate { get; init; }

        [JsonPropertyName("lista")]
        public bool Lista { get; init; }

        [JsonPropertyName("motivo")]
        public string Motivo { get; init; } = string.Empty;
    }

    public sealed class ServerHealthInfo
    {
        [JsonPropertyName("puerto")]
        public int Puerto { get; init; }

        [JsonPropertyName("ejecutandose")]
        public bool Ejecutandose { get; init; }

        [JsonPropertyName("trabajosImpresionEnCurso")]
        public int TrabajosImpresionEnCurso { get; init; }

        [JsonPropertyName("maxTrabajosImpresionEnCurso")]
        public int MaxTrabajosImpresionEnCurso { get; init; }

        [JsonPropertyName("rechazadasPorSaturacion")]
        public long RechazadasPorSaturacion { get; init; }
    }

    public sealed class PrintHealthInfo
    {
        [JsonPropertyName("trabajosTotales")]
        public long TrabajosTotales { get; init; }

        [JsonPropertyName("trabajosFallidos")]
        public long TrabajosFallidos { get; init; }

        [JsonPropertyName("trabajosHistorico")]
        public long TrabajosHistorico { get; init; }

        [JsonPropertyName("fallosHistorico")]
        public long FallosHistorico { get; init; }

        [JsonPropertyName("cortacircuitosAbierto")]
        public bool CortacircuitosAbierto { get; init; }

        [JsonPropertyName("fallosConsecutivos")]
        public int FallosConsecutivos { get; init; }

        [JsonPropertyName("segundosRestantesEnfrio")]
        public int SegundosRestantesEnfrio { get; init; }

        [JsonPropertyName("ultimoErrorUtc")]
        public string? UltimoErrorUtc { get; init; }

        [JsonPropertyName("ultimoMensajeError")]
        public string? UltimoMensajeError { get; init; }
    }

    public sealed class RateLimitInfo
    {
        [JsonPropertyName("solicitudesGlobalesPorSegundo")]
        public int SolicitudesGlobalesPorSegundo { get; init; }

        [JsonPropertyName("ipsPrincipales")]
        public Dictionary<string, int> IpsPrincipales { get; init; } = new();
    }
}
