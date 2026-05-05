using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO.Ports;
using System.Linq;
using Serilog;

namespace ServidorImpresion
{
    public static class DeviceDiscoveryService
    {
        // Nombres que identifican impresoras virtuales — se comparan contra palabras completas
        // (separadas por espacio, guion o inicio/fin de cadena) para no descartar impresoras
        // reales cuyos drivers incluyan accidentalmente alguna de estas palabras.
        private static readonly string[] VirtualPrinterKeywords =
            ["pdf", "fax", "onenote", "microsoft", "document", "writer", "xps", "generic"];

        private static readonly object _cacheLock = new();
        private static IReadOnlyList<string>? _cache;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

        public static IReadOnlyList<string> GetDevices()
        {
            lock (_cacheLock)
            {
                if (_cache != null && DateTime.UtcNow < _cacheExpiry)
                    return _cache;
            }

            var devices = new List<string>();

            // 1) Puertos COM
            string[] ports = SerialPort.GetPortNames().OrderBy(n => n).ToArray();
            devices.AddRange(ports);
            Log.Debug("DeviceDiscoveryService: {Count} puertos COM encontrados: {Ports}",
                ports.Length, string.Join(", ", ports));

            // 2) Impresoras instaladas en Windows (requiere hilo STA)
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                if (IsVirtualPrinter(printer))
                {
                    Log.Debug("DeviceDiscoveryService: descartada impresora virtual '{Name}'", printer);
                    continue;
                }
                devices.Add(printer);
                Log.Debug("DeviceDiscoveryService: impresora aceptada '{Name}'", printer);
            }

            Log.Information("DeviceDiscoveryService: {Count} dispositivos disponibles", devices.Count);

            IReadOnlyList<string> result = devices.AsReadOnly();
            lock (_cacheLock)
            {
                _cache = result;
                _cacheExpiry = DateTime.UtcNow + CacheTtl;
            }
            return result;
        }

        public static void InvalidateCache()
        {
            lock (_cacheLock) { _cache = null; }
        }

        private static bool IsVirtualPrinter(string name)
        {
            // Dividir el nombre en tokens y comprobar si alguno coincide exactamente
            // con una palabra de la lista negra (ej: "EPSON TM-T20" no contiene "document"
            // como token; "Microsoft Print to PDF" sí contiene "microsoft" y "pdf").
            var tokens = name
                .Split([' ', '-', '_', '(', ')'], System.StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToLowerInvariant());

            return tokens.Any(t => VirtualPrinterKeywords.Contains(t));
        }
    }
}
