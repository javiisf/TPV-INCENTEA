using System.IO;
using System.Text;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class ScriptEngineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ScriptEngineTests_" + Guid.NewGuid().ToString("N"));

    public ScriptEngineTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ScriptEngine CreateEngine() => new(_folder);

    private void WriteScript(string name, string source)
        => File.WriteAllText(Path.Combine(_folder, name + ".cs"), source, Encoding.UTF8);

    // ── Script mínimo válido ──────────────────────────────────────────────────

    private const string MinimalScript = """
        using System.Text;
        using ServidorImpresion;
        public class Minimal : ITicketScript {
            public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc) => [0x1B, 0x40];
        }
        """;

    // ── ListScripts ───────────────────────────────────────────────────────────

    [Fact]
    public void ListScripts_EmptyFolder_ReturnsEmpty()
    {
        var engine = CreateEngine();
        Assert.Empty(engine.ListScripts());
    }

    [Fact]
    public void ListScripts_FolderDoesNotExist_ReturnsEmpty()
    {
        var engine = new ScriptEngine(Path.Combine(_folder, "nonexistent"));
        Assert.Empty(engine.ListScripts());
    }

    [Fact]
    public void ListScripts_ReturnsNamesWithoutExtension_Sorted()
    {
        WriteScript("venta", MinimalScript);
        WriteScript("factura", MinimalScript);

        var names = CreateEngine().ListScripts();

        Assert.Equal(["factura", "venta"], names);
    }

    [Fact]
    public void ListScripts_IgnoresNonCsFiles()
    {
        WriteScript("venta", MinimalScript);
        File.WriteAllText(Path.Combine(_folder, "readme.md"), "docs");

        var names = CreateEngine().ListScripts();

        Assert.Equal(["venta"], names);
    }

    // ── Load: archivo no existe ───────────────────────────────────────────────

    [Fact]
    public void Load_MissingScript_ReturnsNull()
    {
        var result = CreateEngine().Load("noexiste");
        Assert.Null(result);
    }

    // ── Load: compilación exitosa ─────────────────────────────────────────────

    [Fact]
    public void Load_ValidScript_ReturnsInstance()
    {
        WriteScript("minimal", MinimalScript);

        var script = CreateEngine().Load("minimal");

        Assert.NotNull(script);
    }

    [Fact]
    public void Load_ValidScript_RendersBytes()
    {
        WriteScript("minimal", MinimalScript);
        var script = CreateEngine().Load("minimal")!;

        byte[] result = script.Render(null!, null!, Encoding.Latin1);

        Assert.Equal([0x1B, 0x40], result);
    }

    // ── Load: error de compilación ────────────────────────────────────────────

    [Fact]
    public void Load_CompileError_ThrowsInvalidOperationException()
    {
        WriteScript("broken", "public class Broken { SYNTAX ERROR }");

        var engine = CreateEngine();
        var ex = Assert.Throws<InvalidOperationException>(() => engine.Load("broken"));

        Assert.Contains("broken.cs", ex.Message);
    }

    // ── Caché ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_SameModifiedTime_ReturnsCachedInstance()
    {
        WriteScript("minimal", MinimalScript);
        var engine = CreateEngine();

        var first  = engine.Load("minimal");
        var second = engine.Load("minimal");

        // misma instancia de ITicketScript porque el mismo Assembly fue reutilizado
        Assert.Equal(first!.GetType().Assembly, second!.GetType().Assembly);
    }

    [Fact]
    public void Load_FileModified_RecompilesScript()
    {
        WriteScript("minimal", MinimalScript);
        var engine = CreateEngine();
        var first = engine.Load("minimal");

        // Simular modificación: reescribir y ajustar timestamp
        string path = Path.Combine(_folder, "minimal.cs");
        File.WriteAllText(path, MinimalScript, Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));

        var second = engine.Load("minimal");

        // Assembly diferente — se recompiló
        Assert.NotEqual(first!.GetType().Assembly, second!.GetType().Assembly);
    }
}
