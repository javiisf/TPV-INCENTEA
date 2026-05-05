using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class PerIpRateLimiterTests
{
    // ── Límite por IP ─────────────────────────────────────────────────────────

    [Fact]
    public void BelowPerIpLimit_AllowsAll()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 5, globalLimit: 100, window: TimeSpan.FromSeconds(10));

        for (int i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("1.2.3.4"));
    }

    [Fact]
    public void AtPerIpLimit_BlocksNext()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 3, globalLimit: 100, window: TimeSpan.FromSeconds(10));

        limiter.TryAcquire("1.2.3.4");
        limiter.TryAcquire("1.2.3.4");
        limiter.TryAcquire("1.2.3.4");

        Assert.False(limiter.TryAcquire("1.2.3.4"));
    }

    [Fact]
    public void DifferentIps_HaveSeparateLimits()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 2, globalLimit: 100, window: TimeSpan.FromSeconds(10));

        limiter.TryAcquire("1.1.1.1");
        limiter.TryAcquire("1.1.1.1");

        // IP diferente no está afectada
        Assert.True(limiter.TryAcquire("2.2.2.2"));
    }

    // ── Límite global ─────────────────────────────────────────────────────────

    [Fact]
    public void GlobalLimit_BlocksAllIpsOnceReached()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 100, globalLimit: 3, window: TimeSpan.FromSeconds(10));

        limiter.TryAcquire("1.1.1.1");
        limiter.TryAcquire("2.2.2.2");
        limiter.TryAcquire("3.3.3.3");

        // Global alcanzado: cualquier IP debe bloquearse
        Assert.False(limiter.TryAcquire("4.4.4.4"));
    }

    // ── IP nula / vacía ───────────────────────────────────────────────────────

    [Fact]
    public void NullIp_TreatedAsUnknown_DoesNotThrow()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 5, globalLimit: 100, window: TimeSpan.FromSeconds(10));
        var result = Record.Exception(() => limiter.TryAcquire(null!));
        Assert.Null(result);
    }

    [Fact]
    public void EmptyIp_TreatedAsUnknown_DoesNotThrow()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 5, globalLimit: 100, window: TimeSpan.FromSeconds(10));
        var result = Record.Exception(() => limiter.TryAcquire(""));
        Assert.Null(result);
    }

    // ── Ventana deslizante ────────────────────────────────────────────────────

    [Fact]
    public async Task AfterWindowExpires_AllowsAgain()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 2, globalLimit: 100, window: TimeSpan.FromMilliseconds(100));

        limiter.TryAcquire("1.1.1.1");
        limiter.TryAcquire("1.1.1.1");
        Assert.False(limiter.TryAcquire("1.1.1.1")); // bloqueado

        await Task.Delay(150); // esperar que expire la ventana

        Assert.True(limiter.TryAcquire("1.1.1.1")); // ventana expirada → permitido
    }

    // ── Estadísticas ──────────────────────────────────────────────────────────

    [Fact]
    public void GetStats_ReflectsCurrentState()
    {
        var limiter = new PerIpRateLimiter(perIpLimit: 10, globalLimit: 100, window: TimeSpan.FromSeconds(10));

        limiter.TryAcquire("1.1.1.1");
        limiter.TryAcquire("1.1.1.1");
        limiter.TryAcquire("2.2.2.2");

        var (globalCount, perIpCounts) = limiter.GetStats();

        Assert.Equal(3, globalCount);
        Assert.Equal(2, perIpCounts["1.1.1.1"]);
        Assert.Equal(1, perIpCounts["2.2.2.2"]);
    }
}
