using System;

namespace ServidorImpresion
{
    /// <summary>
    /// Registro persistente de un trabajo de impresión.
    /// No incluye la vista previa HTML para mantener el tamaño del fichero razonable.
    /// </summary>
    public sealed record PrintHistoryRecord(
        DateTime TimestampUtc,
        bool     Success,
        int      Bytes,
        string   Device,
        string?  ErrorMessage);
}
