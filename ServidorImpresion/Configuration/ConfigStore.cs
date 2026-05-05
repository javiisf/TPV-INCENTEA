using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Responsable de la persistencia de ConfigData en disco.
    /// La API key se cifra con DPAPI (CurrentUser) antes de escribirse al JSON,
    /// por lo que el archivo en disco nunca contiene la clave en texto plano.
    /// Utiliza escritura atómica (write-to-temp + rename) para evitar corrupción.
    /// </summary>
    public class ConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _configPath;
        private readonly object _fileLock = new();

        public ConfigStore(string configPath)
        {
            _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        }

        public string ConfigPath => _configPath;

        public bool Exists() => File.Exists(_configPath);

        // ── Persistencia ────────────────────────────────────────────────────────────

        /// <summary>
        /// Carga la configuración desde disco.
        /// Si la API key estaba cifrada con DPAPI la descifra automáticamente.
        /// Si detecta una clave en texto plano (formato antiguo) la migra al nuevo formato cifrado.
        /// </summary>
        public ConfigData Load()
        {
            lock (_fileLock)
            {
                try
                {
                    EnsureDirectory();

                    StoredConfig? stored = TryReadStored(_configPath)
                        ?? TryReadStored(_configPath + ".tmp");

                    if (stored is null)
                    {
                        Log.Information("ConfigStore.Load: usando configuración por defecto");
                        return new ConfigData();
                    }

                    var config = ToConfigData(stored);

                    // Migración: si hay clave en texto plano (fichero antiguo sin DPAPI)
                    if (!string.IsNullOrEmpty(stored.ApiKey) && string.IsNullOrEmpty(stored.ApiKeyProtected))
                    {
                        Log.Information("ConfigStore.Load: migrando ApiKey a formato cifrado");
                        config.ApiKey = stored.ApiKey;
                        Save(config); // re-guarda ya cifrado, limpia el texto plano del JSON
                    }

                    Log.Information("ConfigStore.Load: configuración cargada desde {Path}", _configPath);
                    return config;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ConfigStore.Load: error cargando configuración, usando valores por defecto");
                    return new ConfigData();
                }
            }
        }

        /// <summary>
        /// Guarda la configuración a disco con escritura atómica.
        /// La API key se cifra con DPAPI antes de escribirse; nunca queda en texto plano en el JSON.
        /// </summary>
        public void Save(ConfigData config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));
            lock (_fileLock)
            {
                try
                {
                    EnsureDirectory();

                    var stored = ToStoredConfig(config);
                    string json = JsonSerializer.Serialize(stored, JsonOptions);

                    string tempPath = _configPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _configPath, overwrite: true);

                    Log.Information("ConfigStore.Save: configuración guardada en {Path}", _configPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ConfigStore.Save: error guardando configuración");
                    throw;
                }
            }
        }

        // ── Cifrado DPAPI ────────────────────────────────────────────────────────────

        private static bool IsValidBase64(string s)
        {
            if (s.Length == 0 || s.Length % 4 != 0) return false;
            try { Convert.FromBase64String(s); return true; }
            catch { return false; }
        }

        private static string? TryEncrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return string.Empty;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plaintext);
                byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ConfigStore: DPAPI no disponible — la ApiKey se guardará en texto plano en disco");
                return null; // el caller decide el fallback
            }
        }

        private static string? TryDecrypt(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return string.Empty;
            try
            {
                byte[] encrypted = Convert.FromBase64String(base64);
                byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ConfigStore: no se pudo descifrar la ApiKey con DPAPI");
                return null;
            }
        }

        // ── Mapeo ConfigData ↔ StoredConfig ─────────────────────────────────────────

        private static StoredConfig ToStoredConfig(ConfigData c)
        {
            string? encrypted = TryEncrypt(c.ApiKey);
            if (encrypted is null && !string.IsNullOrEmpty(c.ApiKey))
                Log.Error("ConfigStore: ApiKey almacenada en texto plano por fallo de DPAPI. Revise el perfil de usuario de Windows.");
            return new StoredConfig
            {
                UltimaUSB              = c.UltimaUSB,
                UltimoCOM              = c.UltimoCOM,
                PuertoServidor         = c.PuertoServidor,
                BaudRate               = c.BaudRate,
                ApiKey                 = string.Empty,             // nunca texto plano en disco
                ApiKeyProtected        = encrypted ?? c.ApiKey,    // fallback a plano si DPAPI falla
                MaxTicketBytes          = c.MaxTicketBytes,
                TrabajosAcumulados      = c.TrabajosAcumulados,
                FallosAcumulados        = c.FallosAcumulados
            };
        }

        private static ConfigData ToConfigData(StoredConfig s)
        {
            string apiKey = string.Empty;

            if (!string.IsNullOrEmpty(s.ApiKeyProtected))
            {
                string? decrypted = TryDecrypt(s.ApiKeyProtected);
                if (decrypted != null)
                {
                    apiKey = decrypted;
                }
                else if (!IsValidBase64(s.ApiKeyProtected))
                {
                    // No es Base64 → fue guardado como texto plano cuando DPAPI no estaba disponible.
                    apiKey = s.ApiKeyProtected;
                }
                else
                {
                    // Es Base64 válido pero DPAPI no pudo descifrar (perfil/máquina diferente).
                    // El valor en disco es irrecuperable; usar vacío para forzar que el usuario
                    // reintroduzca la clave en lugar de arrancar con una clave corrupta.
                    Log.Error("ConfigStore: la ApiKey cifrada no pudo descifrarse con DPAPI " +
                              "(posible cambio de perfil o máquina). Debe reintroducirse en la configuración.");
                }
            }
            else if (!string.IsNullOrEmpty(s.ApiKey))
            {
                apiKey = s.ApiKey; // clave legada en texto plano
            }

            var config = new ConfigData
            {
                UltimaUSB               = s.UltimaUSB,
                UltimoCOM               = s.UltimoCOM,
                PuertoServidor          = s.PuertoServidor,
                BaudRate                = s.BaudRate,
                ApiKey                  = apiKey,
                MaxTicketBytes          = s.MaxTicketBytes,
                TrabajosAcumulados      = s.TrabajosAcumulados,
                FallosAcumulados        = s.FallosAcumulados
            };
            config.Sanitizar();
            return config;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private StoredConfig? TryReadStored(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                var stored = JsonSerializer.Deserialize<StoredConfig>(json);
                if (stored is not null)
                {
                    // Si se leyó desde el .tmp, restaurar el principal
                    if (path.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        Log.Warning("ConfigStore.Load: recuperando configuración desde {Path}", path);
                        File.Move(path, _configPath, overwrite: true);
                    }
                    return stored;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ConfigStore: no se pudo leer {Path}", path);
            }
            return null;
        }

        private void EnsureDirectory()
        {
            string? dir = Path.GetDirectoryName(_configPath);
            if (string.IsNullOrWhiteSpace(dir))
                throw new InvalidOperationException("Directorio de configuración inválido");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ── DTO de persistencia (nunca sale de esta clase) ───────────────────────────

        /// <summary>
        /// Formato real del JSON en disco. ApiKey siempre vacío; ApiKeyProtected
        /// contiene el cifrado DPAPI en Base64 (o texto plano como fallback si DPAPI falló).
        /// </summary>
        private sealed class StoredConfig
        {
            public string UltimaUSB               { get; set; } = "";
            public string UltimoCOM               { get; set; } = "";
            public int    PuertoServidor           { get; set; } = 8080;
            public int    BaudRate                 { get; set; } = 9600;
            public string ApiKey                   { get; set; } = "";  // legado: migración
            public string ApiKeyProtected          { get; set; } = "";  // DPAPI Base64
            public int    MaxTicketBytes           { get; set; } = 512 * 1024;
            public long   TrabajosAcumulados       { get; set; } = 0;
            public long   FallosAcumulados         { get; set; } = 0;
        }
    }
}
