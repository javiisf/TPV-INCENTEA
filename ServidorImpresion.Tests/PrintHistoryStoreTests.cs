using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class PrintHistoryStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public PrintHistoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PrintHistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "history.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string MakeJsonLine(int i) =>
        JsonSerializer.Serialize(new PrintHistoryRecord(
            DateTime.UtcNow.AddSeconds(-i), true, 100, "USB001", null),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    // ── Regresión #12: carga con streaming, no ReadAllLines ──────────────────

    [Fact]
    public void Load_FileWithMoreThanMaxEntries_KeepsOnlyLastN()
    {
        const int maxEntries = 50;
        const int totalLines = 120;

        // Escribir 120 líneas con timestamps distintos para distinguir cuáles son las últimas
        using (var sw = new StreamWriter(_filePath, append: false))
        {
            for (int i = totalLines; i >= 1; i--)
                sw.WriteLine(MakeJsonLine(i)); // i=1 es el más reciente
        }

        using var store = new PrintHistoryStore(_filePath, maxEntries);
        var records = store.GetRecent(maxEntries);

        // Debe haber cargado exactamente maxEntries
        Assert.Equal(maxEntries, records.Length);
    }

    [Fact]
    public void Load_FileWithMoreThanMaxEntries_RewritesFileTrimmed()
    {
        const int maxEntries = 50;
        const int totalLines = 120;

        using (var sw = new StreamWriter(_filePath, append: false))
        {
            for (int i = totalLines; i >= 1; i--)
                sw.WriteLine(MakeJsonLine(i));
        }

        using var store = new PrintHistoryStore(_filePath, maxEntries);

        // El fichero debe haberse reescrito con solo maxEntries líneas
        int lineCount = File.ReadAllLines(_filePath).Count(l => !string.IsNullOrWhiteSpace(l));
        Assert.Equal(maxEntries, lineCount);
    }

    [Fact]
    public void Load_FileWithExactlyMaxEntries_LoadsAll()
    {
        const int maxEntries = 20;

        using (var sw = new StreamWriter(_filePath, append: false))
        {
            for (int i = maxEntries; i >= 1; i--)
                sw.WriteLine(MakeJsonLine(i));
        }

        using var store = new PrintHistoryStore(_filePath, maxEntries);
        var records = store.GetRecent(maxEntries);

        Assert.Equal(maxEntries, records.Length);
    }

    [Fact]
    public void Load_FileWithCorruptLines_SkipsCorruptAndLoadsValid()
    {
        using (var sw = new StreamWriter(_filePath, append: false))
        {
            sw.WriteLine(MakeJsonLine(2));
            sw.WriteLine("{ esto no es json }");
            sw.WriteLine(MakeJsonLine(1));
        }

        using var store = new PrintHistoryStore(_filePath, maxEntries: 100);
        var records = store.GetRecent(100);

        Assert.Equal(2, records.Length);
    }

    // ── Record + GetRecent ────────────────────────────────────────────────────

    [Fact]
    public void Record_AddsEntryAndGetRecentReturnsIt()
    {
        using var store = new PrintHistoryStore(_filePath, maxEntries: 10);
        store.Record(success: true, bytes: 200, device: "COM3", errorMessage: null);

        var records = store.GetRecent(10);
        Assert.Single(records);
        Assert.True(records[0].Success);
        Assert.Equal(200, records[0].Bytes);
    }

    [Fact]
    public void Record_WithErrorMessage_PersistsMessage()
    {
        using var store = new PrintHistoryStore(_filePath, maxEntries: 10);
        store.Record(success: false, bytes: 100, device: "COM3", errorMessage: "Timeout de impresora");

        var records = store.GetRecent(10);
        Assert.Single(records);
        Assert.False(records[0].Success);
        Assert.Equal("Timeout de impresora", records[0].ErrorMessage);
    }

    [Fact]
    public void GetRecent_ReturnsNewestFirst()
    {
        using var store = new PrintHistoryStore(_filePath, maxEntries: 10);
        store.Record(success: true, bytes: 10, device: "D", errorMessage: null);
        store.Record(success: true, bytes: 20, device: "D", errorMessage: null);

        var records = store.GetRecent(10);
        Assert.Equal(20, records[0].Bytes); // más reciente primero
        Assert.Equal(10, records[1].Bytes);
    }

    [Fact]
    public void Record_ExceedingMaxEntries_EvictsOldest()
    {
        const int maxEntries = 5;
        using var store = new PrintHistoryStore(_filePath, maxEntries);

        for (int i = 0; i < 8; i++)
            store.Record(success: true, bytes: i * 10, device: "USB", errorMessage: null);

        var records = store.GetRecent(10);
        Assert.Equal(maxEntries, records.Length);
    }
}
