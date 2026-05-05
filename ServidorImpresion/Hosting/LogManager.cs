using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ServidorImpresion
{
    /// <summary>
    /// Gestiona la configuración centralizada de Serilog para logging.
    /// </summary>
    public static class LogManager
    {
        private static bool _initialized = false;
        private static readonly LoggingLevelSwitch _levelSwitch = new(LogEventLevel.Information);

        /// <summary>
        /// Inicializa Serilog con sinks a archivo y consola.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            string logPath = GetLogDirectory();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "ServidorImpresion")
                .WriteTo.File(
                    path: Path.Combine(logPath, "impresion-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] ({Application}) [Req:{RequestId}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.Console(
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] [Req:{RequestId}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .CreateLogger();

            _initialized = true;
            Log.Information("LogManager: sistema de logging inicializado. LogPath={LogPath}", logPath);
        }

        /// <summary>
        /// Obtiene o crea el directorio de logs en %AppData%.
        /// </summary>
        private static string GetLogDirectory()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDir = Path.Combine(appDataPath, "ServidorImpresion", "Logs");

            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            return logDir;
        }

        /// <summary>
        /// Cambia el nivel de log en caliente. Valores válidos: Debug, Information, Warning, Error.
        /// </summary>
        public static void SetLevel(string level)
        {
            _levelSwitch.MinimumLevel = level switch
            {
                "Debug"       => LogEventLevel.Debug,
                "Warning"     => LogEventLevel.Warning,
                "Error"       => LogEventLevel.Error,
                _             => LogEventLevel.Information
            };
            Log.Information("LogManager: nivel de log cambiado a {Level}", _levelSwitch.MinimumLevel);
        }

        /// <summary>
        /// Cierra y vacía Serilog.
        /// </summary>
        public static void Close()
        {
            Log.CloseAndFlush();
        }
    }
}
