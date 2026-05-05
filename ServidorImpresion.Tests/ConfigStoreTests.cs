using System;
using System.IO;
using System.Text.Json;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ConfigStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _store = new ConfigStore(_configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Exists ────────────────────────────────────────────────────────────────

    [Fact]
    public void Exists_WhenNoFile_ReturnsFalse()
    {
        Assert.False(_store.Exists());
    }

    [Fact]
    public void Exists_AfterSave_ReturnsTrue()
    {
        _store.Save(new ConfigData());
        Assert.True(_store.Exists());
    }

    // ── ConfigPath ────────────────────────────────────────────────────────────

    [Fact]
    public void ConfigPath_ReturnsProvidedPath()
    {
        Assert.Equal(_configPath, _store.ConfigPath);
    }

    // ── Load: sin fichero ─────────────────────────────────────────────────────

    [Fact]
    public void Load_WhenNoFile_ReturnsDefaultConfig()
    {
        var config = _store.Load();
        Assert.Equal(8080, config.PuertoServidor);
        Assert.Equal("", config.ApiKey);
        Assert.Equal(9600, config.BaudRate);
    }

    // ── Save + Load: round-trip ───────────────────────────────────────────────

    [Fact]
    public void SaveAndLoad_PreservesScalarValues()
    {
        var original = new ConfigData
        {
            PuertoServidor = 9090,
            UltimoCOM = "COM3",
            BaudRate = 115200,
            TrabajosAcumulados = 42,
            FallosAcumulados = 1
        };
        _store.Save(original);
        var loaded = _store.Load();

        Assert.Equal(9090, loaded.PuertoServidor);
        Assert.Equal("COM3", loaded.UltimoCOM);
        Assert.Equal(115200, loaded.BaudRate);
        Assert.Equal(42, loaded.TrabajosAcumulados);
        Assert.Equal(1, loaded.FallosAcumulados);
    }

    [Fact]
    public void SaveAndLoad_UltimaUSB_Preserved()
    {
        var original = new ConfigData { UltimaUSB = "PrinterUSB001" };
        _store.Save(original);
        var loaded = _store.Load();
        Assert.Equal("PrinterUSB001", loaded.UltimaUSB);
    }

    // ── Cifrado de ApiKey ─────────────────────────────────────────────────────

    [Fact]
    public void Save_ApiKey_NotStoredAsPlaintextInJson()
    {
        _store.Save(new ConfigData { ApiKey = "mi-clave-secreta" });

        string json = File.ReadAllText(_configPath);
        var doc = JsonDocument.Parse(json);

        // El campo ApiKey en disco debe estar vacío
        string apiKeyRaw = doc.RootElement.GetProperty("ApiKey").GetString() ?? "";
        Assert.Equal("", apiKeyRaw);

        // ApiKeyProtected debe ser no vacío (cifrado DPAPI en Base64)
        string apiKeyProtected = doc.RootElement.GetProperty("ApiKeyProtected").GetString() ?? "";
        Assert.NotEmpty(apiKeyProtected);
    }

    [Fact]
    public void SaveAndLoad_ApiKey_RoundTrips()
    {
        _store.Save(new ConfigData { ApiKey = "clave-de-prueba-123" });
        var loaded = _store.Load();
        Assert.Equal("clave-de-prueba-123", loaded.ApiKey);
    }

    [Fact]
    public void SaveAndLoad_EmptyApiKey_RemainsEmpty()
    {
        _store.Save(new ConfigData { ApiKey = "" });
        var loaded = _store.Load();
        Assert.Equal("", loaded.ApiKey);
    }

    // ── Migración de clave en texto plano ─────────────────────────────────────

    [Fact]
    public void Load_PlaintextApiKey_MigratesToEncrypted()
    {
        // Simular un fichero antiguo con ApiKey en texto plano (sin ApiKeyProtected)
        const string oldJson = """
            {
                "UltimaUSB": "",
                "UltimoCOM": "",
                "PuertoServidor": 8080,
                "BaudRate": 9600,
                "ApiKey": "clave-legada",
                "ApiKeyProtected": "",
                "MaxTicketBytes": 524288,
                "HabilitarPreviewTickets": false,
                "TrabajosAcumulados": 0,
                "FallosAcumulados": 0
            }
            """;
        File.WriteAllText(_configPath, oldJson);

        var loaded = _store.Load();

        // La clave debe resolverse correctamente
        Assert.Equal("clave-legada", loaded.ApiKey);

        // Después de la migración, ApiKey en disco debe estar vacío
        string newJson = File.ReadAllText(_configPath);
        var doc = JsonDocument.Parse(newJson);
        Assert.Equal("", doc.RootElement.GetProperty("ApiKey").GetString());
    }

    // ── Robustez ──────────────────────────────────────────────────────────────

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultConfig()
    {
        File.WriteAllText(_configPath, "{ esto no es json valido }}}");
        var config = _store.Load();
        Assert.Equal(8080, config.PuertoServidor);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaultConfig()
    {
        File.WriteAllText(_configPath, "");
        var config = _store.Load();
        Assert.Equal(8080, config.PuertoServidor);
    }

    [Fact]
    public void Save_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Save(null!));
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigStore(null!));
    }

    // ── Escritura atómica ─────────────────────────────────────────────────────

    [Fact]
    public void Save_AfterSave_TempFileIsGone()
    {
        _store.Save(new ConfigData());
        // El fichero .tmp no debe quedar tras un save exitoso
        Assert.False(File.Exists(_configPath + ".tmp"));
    }

    // ── Regresión #9: DPAPI fallback ─────────────────────────────────────────

    [Fact]
    public void Load_ApiKeyProtected_PlaintextFallback_UsesRawValue()
    {
        // Simula el caso donde DPAPI no estaba disponible al guardar y se almacenó
        // la clave en texto plano en ApiKeyProtected (valor no Base64).
        const string plaintextKey = "clave-sin-cifrar-plana";
        string json = $$"""
            {
                "UltimaUSB": "",
                "UltimoCOM": "",
                "PuertoServidor": 8080,
                "BaudRate": 9600,
                "ApiKey": "",
                "ApiKeyProtected": "{{plaintextKey}}",
                "MaxTicketBytes": 524288,
                "TrabajosAcumulados": 0,
                "FallosAcumulados": 0
            }
            """;
        File.WriteAllText(_configPath, json);

        var loaded = _store.Load();

        // El valor no es Base64 → debe usarse como texto plano (fallback de guardado)
        Assert.Equal(plaintextKey, loaded.ApiKey);
    }

    [Fact]
    public void Load_ApiKeyProtected_ValidBase64ButNotDpapi_ReturnsEmptyApiKey()
    {
        // Simula el caso de cambio de perfil/máquina: el valor es Base64 válido
        // pero ProtectedData.Unprotect falla → la clave es irrecuperable → vacío.
        // Usamos Base64 de datos aleatorios que no son un blob DPAPI válido.
        string fakeEncrypted = Convert.ToBase64String(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        string json = $$"""
            {
                "UltimaUSB": "",
                "UltimoCOM": "",
                "PuertoServidor": 8080,
                "BaudRate": 9600,
                "ApiKey": "",
                "ApiKeyProtected": "{{fakeEncrypted}}",
                "MaxTicketBytes": 524288,
                "TrabajosAcumulados": 0,
                "FallosAcumulados": 0
            }
            """;
        File.WriteAllText(_configPath, json);

        var loaded = _store.Load();

        // Base64 válido pero no DPAPI → irrecuperable → vacío en lugar de ciphertext corrupto
        Assert.Equal("", loaded.ApiKey);
    }

    // ── Sanitización en Load ──────────────────────────────────────────────────

    [Fact]
    public void Load_OutOfRangePort_GetsSanitized()
    {
        const string json = """
            {
                "UltimaUSB": "",
                "UltimoCOM": "",
                "PuertoServidor": -5,
                "BaudRate": 9600,
                "ApiKey": "",
                "ApiKeyProtected": "",
                "MaxTicketBytes": 524288,
                "HabilitarPreviewTickets": false,
                "TrabajosAcumulados": 0,
                "FallosAcumulados": 0
            }
            """;
        File.WriteAllText(_configPath, json);
        var config = _store.Load();
        Assert.Equal(8080, config.PuertoServidor);
    }
}
