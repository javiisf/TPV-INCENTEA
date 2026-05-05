using System.IO;
using System.Text;
using System.Text.Json;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class ScriptEndpointHandlerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MappingTests_" + Guid.NewGuid().ToString("N"));

    public ScriptEndpointHandlerTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteMappingJson(string content)
        => File.WriteAllText(Path.Combine(_folder, "mapping.json"), content, Encoding.UTF8);

    // ── LoadMapping ───────────────────────────────────────────────────────────

    [Fact]
    public void LoadMapping_NoFile_ReturnsDefaults()
    {
        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("empresaData", eKey);
        Assert.Equal("ticketData",  tKey);
    }

    [Fact]
    public void LoadMapping_ValidFile_ReturnsConfiguredKeys()
    {
        WriteMappingJson("""{ "empresaKey": "empresa", "ticketKey": "ticket" }""");

        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("empresa", eKey);
        Assert.Equal("ticket",  tKey);
    }

    [Fact]
    public void LoadMapping_CorruptJson_ReturnsDefaults()
    {
        WriteMappingJson("{ NOT VALID JSON }");

        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("empresaData", eKey);
        Assert.Equal("ticketData",  tKey);
    }

    [Fact]
    public void LoadMapping_MissingEmpresaKey_ReturnsDefaultForThat()
    {
        WriteMappingJson("""{ "ticketKey": "datos" }""");

        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("empresaData", eKey);
        Assert.Equal("datos",       tKey);
    }

    [Fact]
    public void LoadMapping_MissingTicketKey_ReturnsDefaultForThat()
    {
        WriteMappingJson("""{ "empresaKey": "company" }""");

        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("company",    eKey);
        Assert.Equal("ticketData", tKey);
    }

    [Fact]
    public void LoadMapping_EmptyStringValue_ReturnsDefault()
    {
        WriteMappingJson("""{ "empresaKey": "", "ticketKey": "" }""");

        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("empresaData", eKey);
        Assert.Equal("ticketData",  tKey);
    }

    [Fact]
    public void LoadMapping_NonStringValues_ReturnsDefaults()
    {
        WriteMappingJson("""{ "empresaKey": 42, "ticketKey": true }""");

        var (eKey, tKey) = ScriptEndpointHandler.LoadMapping(_folder);

        Assert.Equal("empresaData", eKey);
        Assert.Equal("ticketData",  tKey);
    }

    // ── ResolveProperty ───────────────────────────────────────────────────────

    private static JsonElement ParseRoot(string json)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ResolveProperty_KeyExistsAsObject_ReturnsElement()
    {
        var root = ParseRoot("""{ "empresaData": { "cif": "B12345678" } }""");

        var result = ScriptEndpointHandler.ResolveProperty(root, "empresaData", "empresaData");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("B12345678", result.GetProperty("cif").GetString());
    }

    [Fact]
    public void ResolveProperty_KeyIsString_ThrowsKeyNotFoundException()
    {
        // "ticket": "001" es el caso clásico de conflicto de nombre
        var root = ParseRoot("""{ "ticket": "001", "ticketData": { "total": 10 } }""");

        Assert.Throws<KeyNotFoundException>(
            () => ScriptEndpointHandler.ResolveProperty(root, "ticket", "ticketData"));
    }

    [Fact]
    public void ResolveProperty_KeyNotFound_ThrowsKeyNotFoundException()
    {
        var root = ParseRoot("""{ "otraCosa": { "x": 1 } }""");

        Assert.Throws<KeyNotFoundException>(
            () => ScriptEndpointHandler.ResolveProperty(root, "empresaData", "empresaData"));
    }

    [Fact]
    public void ResolveProperty_ErrorMessageIncludesConfiguredKey()
    {
        var root = ParseRoot("""{ "otraCosa": {} }""");

        var ex = Assert.Throws<KeyNotFoundException>(
            () => ScriptEndpointHandler.ResolveProperty(root, "miClaveCustom", "empresaData"));

        Assert.Contains("miClaveCustom", ex.Message);
    }

    [Fact]
    public void ResolveProperty_KeyIsArray_ThrowsKeyNotFoundException()
    {
        var root = ParseRoot("""{ "ticketData": [1, 2, 3] }""");

        Assert.Throws<KeyNotFoundException>(
            () => ScriptEndpointHandler.ResolveProperty(root, "ticketData", "ticketData"));
    }
}
