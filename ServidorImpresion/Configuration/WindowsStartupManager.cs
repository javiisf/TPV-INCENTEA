using System;
using System.Windows.Forms;
using Microsoft.Win32;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Gestiona el registro de la aplicación en el inicio automático de Windows
    /// mediante la clave HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// No requiere privilegios de administrador.
    /// </summary>
    public static class WindowsStartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName    = "ServidorImpresion";

        /// <summary>
        /// Indica si la app está registrada para arrancar con Windows.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(AppName) is string path
                    && !string.IsNullOrEmpty(path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowsStartupManager.IsEnabled: no se pudo leer el registro");
                return false;
            }
        }

        /// <summary>
        /// Registra la app para que arranque automáticamente al iniciar sesión.
        /// </summary>
        public static void Enable()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? throw new InvalidOperationException("No se pudo abrir la clave Run del registro");

                key.SetValue(AppName, exePath);
                Log.Information("WindowsStartupManager: autostart activado. Ruta={Path}", exePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WindowsStartupManager.Enable: error escribiendo en el registro");
                throw;
            }
        }

        /// <summary>
        /// Elimina el registro de arranque automático.
        /// </summary>
        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key?.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName);
                    Log.Information("WindowsStartupManager: autostart desactivado");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WindowsStartupManager.Disable: error eliminando del registro");
                throw;
            }
        }
    }
}
