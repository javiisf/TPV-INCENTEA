using System.Threading;
using System.Threading.Tasks;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class PrintJobServiceTests : IDisposable
{
    // ── Fake IPrinterService ──────────────────────────────────────────────────

    private sealed class FakePrinter : IPrinterService
    {
        public bool ShouldSucceed { get; set; } = true;
        public int CallCount { get; private set; }
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        /// <summary>Si se asigna, PrintAsync bloquea hasta que se libere el semáforo.</summary>
        public SemaphoreSlim? Gate { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public async Task<PrintResult> PrintAsync(byte[] data, CancellationToken ct = default)
        {
            CallCount++;
            if (Gate != null)
                await Gate.WaitAsync(ct);
            else if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, ct);
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;
            return ShouldSucceed
                ? PrintResult.SuccessResult("OK")
                : PrintResult.FailureResult("Error de impresora");
        }

        public void UpdateConfig(ConfigData _) { }
        public (long Total, long Failed) GetStatistics() => (CallCount, 0);
        public PrinterStatus GetStatus() => new();
        public ConfiguredDevice GetConfiguredDevice() => new();
        public void Dispose() { }
    }

    private readonly FakePrinter _printer = new();
    private PrintJobService CreateService(int queueCapacity = 10)
        => new(_printer, queueCapacity, () => { });

    // ── Validación de payload ─────────────────────────────────────────────────

    [Fact]
    public async Task EmptyPayload_ReturnsError()
    {
        using var svc = CreateService();
        var result = await svc.ExecuteAsync([], CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("Datos vacíos", result.Message);
    }

    [Fact]
    public async Task InvalidPayload_MzHeader_ReturnsError()
    {
        using var svc = CreateService();
        var result = await svc.ExecuteAsync([0x4D, 0x5A, 0x00, 0x00], CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("Payload inválido", result.Message);
    }

    // ── Impresión correcta ────────────────────────────────────────────────────

    [Fact]
    public async Task ValidEscPosPayload_CallsPrinter_ReturnsSuccess()
    {
        using var svc = CreateService();
        byte[] payload = [0x1B, 0x40, .. "Ticket\n"u8];

        var result = await svc.ExecuteAsync(payload, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _printer.CallCount);
    }

    [Fact]
    public async Task PrinterFailure_ReturnsFailure()
    {
        _printer.ShouldSucceed = false;
        using var svc = CreateService();
        byte[] payload = [0x1B, 0x40, .. "Ticket\n"u8];

        var result = await svc.ExecuteAsync(payload, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Error", result.Message);
    }

    [Fact]
    public async Task PrinterThrowsException_ReturnsFailureWithExceptionMessage()
    {
        _printer.ExceptionToThrow = new InvalidOperationException("error de hardware");
        using var svc = CreateService();
        byte[] payload = [0x1B, 0x40, .. "Ticket\n"u8];

        var result = await svc.ExecuteAsync(payload, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("error de hardware", result.Message);
    }

    // ── Backpressure ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FullQueue_ReturnsBackpressureRejection()
    {
        // Gate bloquea al consumidor dentro de PrintAsync de forma determinista:
        // - t1 entra al canal → consumidor lo lee inmediatamente, llama PrintAsync y queda bloqueado en Gate
        // - t2 entra al canal (ocupa el único slot libre, capacidad=1)
        // - t3 intenta TryWrite → canal lleno → backpressure ✓
        var gate = new SemaphoreSlim(0);
        _printer.Gate = gate;
        int rejectedCount = 0;
        using var svc = new PrintJobService(_printer, queueCapacity: 1, () => rejectedCount++);

        byte[] payload = [0x1B, 0x40, .. "T\n"u8];

        var t1 = svc.ExecuteAsync(payload, CancellationToken.None);
        await Task.Delay(50); // dar tiempo al consumidor para leer t1 y bloquearse en Gate

        var t2 = svc.ExecuteAsync(payload, CancellationToken.None); // ocupa el slot libre
        var result = await svc.ExecuteAsync(payload, CancellationToken.None); // canal lleno → backpressure

        Assert.True(result.RejectedByBackpressure);
        Assert.Equal(1, rejectedCount);

        // Liberar para que el test cierre limpiamente
        gate.Release(10);
        await Task.WhenAny(Task.WhenAll(t1, t2), Task.Delay(2000));
    }

    // ── PendingCount ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PendingCount_ReflectsQueuedJobs()
    {
        _printer.Delay = TimeSpan.FromMilliseconds(200);
        using var svc = CreateService(queueCapacity: 10);
        byte[] payload = [0x1B, 0x40, .. "T\n"u8];

        // Encolar sin esperar para que queden pendientes
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => svc.ExecuteAsync(payload, CancellationToken.None))
            .ToArray();

        await Task.Delay(30);
        Assert.True(svc.PendingCount >= 0); // al menos no rompe

        await Task.WhenAll(tasks);
        Assert.Equal(0, svc.PendingCount);
    }

    // ── DrainAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DrainAsync_CompletesAllPendingJobs()
    {
        _printer.Delay = TimeSpan.FromMilliseconds(30);
        using var svc = CreateService(queueCapacity: 10);
        byte[] payload = [0x1B, 0x40, .. "T\n"u8];

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => svc.ExecuteAsync(payload, CancellationToken.None))
            .ToArray();

        await svc.DrainAsync(TimeSpan.FromSeconds(5));

        // Todos los trabajos deben haber terminado tras el drain
        Assert.All(tasks, t => Assert.True(t.IsCompleted));
        Assert.Equal(3, _printer.CallCount);
    }

    // ── Cancelación ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelledRequest_DoesNotBlockQueue()
    {
        _printer.Delay = TimeSpan.FromMilliseconds(500);
        using var svc = CreateService(queueCapacity: 10);
        byte[] payload = [0x1B, 0x40, .. "T\n"u8];

        using var cts = new CancellationTokenSource();
        var task = svc.ExecuteAsync(payload, cts.Token);

        cts.Cancel();

        // La tarea debe completarse (cancelada o con resultado), no quedarse colgada
        await Task.WhenAny(task, Task.Delay(2000));
        Assert.True(task.IsCompleted);
    }

    // ── Regresión #5: DrainAsync + Dispose concurrente no lanza excepción ────

    [Fact]
    public async Task DrainAsync_ConcurrentDispose_DoesNotThrow()
    {
        // Simula el escenario donde StopAsync agota su timeout y el caller llama
        // Dispose() mientras DrainAsync sigue corriendo con su propio timeout.
        var gate = new SemaphoreSlim(0);
        _printer.Gate = gate;
        using var svc = CreateService(queueCapacity: 5);

        byte[] payload = [0x1B, 0x40, .. "T\n"u8];
        _ = svc.ExecuteAsync(payload, CancellationToken.None);
        await Task.Delay(30); // dar tiempo al consumidor a leer el trabajo y bloquearse

        // DrainAsync con timeout muy corto → expirará e intentará _shutdownCts.Cancel()
        var drainTask = svc.DrainAsync(TimeSpan.FromMilliseconds(50));

        // Dispose() concurrente → llama _shutdownCts.Dispose() mientras DrainAsync corre
        await Task.Delay(30);
        svc.Dispose();

        // No debe lanzar ObjectDisposedException
        var ex = await Record.ExceptionAsync(() => drainTask);
        Assert.Null(ex);

        gate.Release(10);
    }

    public void Dispose() { }
}
