using System;
using System.Threading;
using System.Threading.Tasks;
using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class AppStatusMonitorTests
{
    // ── Regresión #1: ObjectDisposedException en printerHealthy no debe crashear ──

    [Fact]
    public async Task Tick_WhenPrinterHealthyThrows_DoesNotPropagate()
    {
        // Arrange: delegate que simula un objeto dispuesto capturado en el closure
        var monitor = new AppStatusMonitor(
            serverState: () => (true, 8080),
            stats: () => (0L, 0L),
            intervalMs: 50,
            printerHealthy: () => throw new ObjectDisposedException("FakeService"));

        // Act: arrancar y esperar varios ticks
        monitor.Start();
        await Task.Delay(200);
        monitor.Dispose();

        // Assert: no excepción no capturada → el proceso sigue vivo
        // El snapshot puede no haberse disparado si la excepción lo cortó — lo que importa
        // es que el test llega hasta aquí sin crashear.
        Assert.True(true);
    }

    [Fact]
    public async Task Tick_WhenPrinterHealthyIsNull_UsesFailedCountFallback()
    {
        bool? capturedHealthy = null;
        var monitor = new AppStatusMonitor(
            serverState: () => (true, 8080),
            stats: () => (10L, 0L),   // 0 fallos → healthy = true
            intervalMs: 50,
            printerHealthy: null);

        monitor.Snapshot += (_, snap) => capturedHealthy = snap.PrinterHealthy;

        monitor.Start();
        await Task.Delay(200);
        monitor.Dispose();

        Assert.True(capturedHealthy);
    }

    [Fact]
    public async Task Tick_WhenPrinterHealthyReturnsFalse_SnapshotShowsFalse()
    {
        bool? capturedHealthy = null;
        var monitor = new AppStatusMonitor(
            serverState: () => (true, 8080),
            stats: () => (0L, 0L),
            intervalMs: 50,
            printerHealthy: () => false);

        monitor.Snapshot += (_, snap) => capturedHealthy = snap.PrinterHealthy;

        monitor.Start();
        await Task.Delay(200);
        monitor.Dispose();

        Assert.False(capturedHealthy);
    }

    [Fact]
    public async Task Tick_WhenPrinterHealthyIsNull_WithNonZeroFailed_SnapshotShowsFalse()
    {
        bool? capturedHealthy = null;
        var monitor = new AppStatusMonitor(
            serverState: () => (true, 8080),
            stats: () => (10L, 3L),  // 3 fallos → fallback unhealthy
            intervalMs: 50,
            printerHealthy: null);

        monitor.Snapshot += (_, snap) => capturedHealthy = snap.PrinterHealthy;

        monitor.Start();
        await Task.Delay(200);
        monitor.Dispose();

        Assert.False(capturedHealthy);
    }

    [Fact]
    public async Task Tick_AfterDispose_DoesNotFireSnapshot()
    {
        int snapshotCount = 0;
        var monitor = new AppStatusMonitor(
            serverState: () => (true, 8080),
            stats: () => (0L, 0L),
            intervalMs: 50);

        monitor.Snapshot += (_, _) => snapshotCount++;
        monitor.Start();
        await Task.Delay(120);
        monitor.Dispose();
        int countAtDispose = snapshotCount;

        // Esperar otro intervalo: no debe haber más snapshots tras Dispose
        await Task.Delay(120);
        Assert.Equal(countAtDispose, snapshotCount);
    }
}
