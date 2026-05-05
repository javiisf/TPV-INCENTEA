using System;
using System.Management;
using System.Threading.Tasks;

namespace ServidorImpresion
{
    public static class PrinterStatusChecker
    {
        public static Task<(bool Ready, string Reason)> TryGetPrinterReadyAsync(string printerName)
        {
            return Task.Run(() =>
            {
                bool ready = TryGetPrinterReady(printerName, out string reason);
                return (ready, reason);
            });
        }

        private static T WmiGet<T>(ManagementObject mo, string field, T fallback)
        {
            var val = mo[field];
            if (val is null || val is DBNull) return fallback;
            return (T)Convert.ChangeType(val, typeof(T));
        }

        public static bool TryGetPrinterReady(string printerName, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(printerName))
            {
                reason = "Nombre de impresora vacío";
                return false;
            }

            try
            {
                string escaped = printerName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    $"SELECT WorkOffline, PrinterStatus, DetectedErrorState, ExtendedPrinterStatus, ExtendedDetectedErrorState FROM Win32_Printer WHERE Name = \"{escaped}\"");

                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                using (mo)
                {
                    bool workOffline = WmiGet(mo, "WorkOffline", false);
                    var printerStatus = WmiGet(mo, "PrinterStatus", 0);
                    var detectedErrorState = WmiGet(mo, "DetectedErrorState", 0);
                    var extendedPrinterStatus = WmiGet(mo, "ExtendedPrinterStatus", 0);
                    var extendedDetectedErrorState = WmiGet(mo, "ExtendedDetectedErrorState", 0);

                    // 1. Verificación de Offline (La más importante para Windows)
                    if (workOffline || printerStatus == 7)
                    {
                        reason = "Impresora sin conexión (Offline)";
                        return false;
                    }

                    // 2. Validación de DetectedErrorState
                  
                    if (detectedErrorState != 0 && detectedErrorState != 1 && detectedErrorState != 2)
                    {
                        reason = $"Error detectado (Estado: {detectedErrorState})";
                        return false;
                    }

                    // 3. Validación de ExtendedDetectedErrorState
                    
                    if (extendedDetectedErrorState != 0 && extendedDetectedErrorState != 1 && extendedDetectedErrorState != 2)
                    {
                        reason = $"Error extendido (Estado: {extendedDetectedErrorState})";
                        return false;
                    }

                    
                    // Consideramos "Ready" si el status es 3 (Ociosa), 4 (Imprimiendo) o 2 (Unknown pero no Offline).
                    bool statusEsBueno = (printerStatus == 3 || printerStatus == 4 || printerStatus == 2 || printerStatus == 1);

                    // Solo bloqueamos si el Extended dice explícitamente Offline (7) o Detenida (6)
                    if (statusEsBueno && extendedPrinterStatus != 7 && extendedPrinterStatus != 6)
                    {
                        return true; // ¡ESTÁ CONECTADA!
                    }

                    reason = $"No lista (Status: {printerStatus}, Ext: {extendedPrinterStatus})";
                    return false;
                }

                reason = "No encontrada";
                return false;
            }
            catch (Exception ex)
            {
                reason = $"Error WMI: {ex.Message}";
                return false;
            }
        }
    }
}