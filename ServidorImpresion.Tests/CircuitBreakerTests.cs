using ServidorImpresion;

namespace ServidorImpresion.Tests;

public class CircuitBreakerTests
{
    // ── Estado Closed ─────────────────────────────────────────────────────────

    [Fact]
    public void NewBreaker_IsClosed_AllowsRequests()
    {
        var cb = new CircuitBreaker(threshold: 3);
        Assert.Null(cb.TryAcquire());
    }

    [Fact]
    public void BelowThreshold_RemainsOpen()
    {
        var cb = new CircuitBreaker(threshold: 3);
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");
        // 2 fallos < threshold 3 → sigue cerrado
        Assert.Null(cb.TryAcquire());
    }

    // ── Apertura del circuito ─────────────────────────────────────────────────

    [Fact]
    public void AtThreshold_CircuitOpens_RejectsRequests()
    {
        var cb = new CircuitBreaker(threshold: 3, cooldown: TimeSpan.FromSeconds(60));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");
        cb.RecordFailure("dev"); // llega al threshold
        Assert.NotNull(cb.TryAcquire());
    }

    [Fact]
    public void OpenCircuit_GetSnapshot_ReportsOpen()
    {
        var cb = new CircuitBreaker(threshold: 2, cooldown: TimeSpan.FromSeconds(60));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        var (isOpen, failures, _) = cb.GetSnapshot();
        Assert.True(isOpen);
        Assert.Equal(2, failures);
    }

    // ── Estado Half-Open ──────────────────────────────────────────────────────

    [Fact]
    public async Task AfterCooldown_AllowsOneProbe()
    {
        var cb = new CircuitBreaker(
            threshold: 2,
            cooldown: TimeSpan.FromMilliseconds(50),
            probeInterval: TimeSpan.FromMilliseconds(10));

        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        await Task.Delay(100); // esperar cooldown

        // Primera llamada tras cooldown → debe ser la sonda (null)
        Assert.Null(cb.TryAcquire());
    }

    [Fact]
    public async Task AfterCooldown_SecondConcurrentCall_IsRejected()
    {
        var cb = new CircuitBreaker(
            threshold: 2,
            cooldown: TimeSpan.FromMilliseconds(50),
            probeInterval: TimeSpan.FromSeconds(60)); // probe interval largo

        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        await Task.Delay(100); // esperar cooldown

        cb.TryAcquire(); // primera sonda se lleva el slot
        string? result = cb.TryAcquire(); // segunda debe ser rechazada
        Assert.NotNull(result);
    }

    // ── Recuperación ─────────────────────────────────────────────────────────

    [Fact]
    public void RecordSuccess_AfterOpen_ClosesCircuit()
    {
        var cb = new CircuitBreaker(threshold: 2, cooldown: TimeSpan.FromSeconds(60));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        bool wasOpen = cb.RecordSuccess();

        Assert.True(wasOpen);
        Assert.Null(cb.TryAcquire()); // circuito cerrado de nuevo
    }

    [Fact]
    public void RecordSuccess_WhenClosed_ReturnsFalse()
    {
        var cb = new CircuitBreaker(threshold: 5);
        bool wasOpen = cb.RecordSuccess();
        Assert.False(wasOpen);
    }

    // ── GetSnapshot ───────────────────────────────────────────────────────────

    [Fact]
    public void ClosedCircuit_Snapshot_IsNotOpen()
    {
        var cb = new CircuitBreaker(threshold: 5);
        var (isOpen, failures, remaining) = cb.GetSnapshot();
        Assert.False(isOpen);
        Assert.Equal(0, failures);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void OpenCircuit_Snapshot_HasPositiveRemaining()
    {
        var cb = new CircuitBreaker(threshold: 2, cooldown: TimeSpan.FromSeconds(30));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        var (isOpen, _, remaining) = cb.GetSnapshot();
        Assert.True(isOpen);
        Assert.True(remaining > 0);
    }

    [Fact]
    public void RecordFailure_WhenAlreadyOpen_ExtendsCooldown()
    {
        var cb = new CircuitBreaker(threshold: 2, cooldown: TimeSpan.FromSeconds(60));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev"); // abre el circuito

        cb.RecordFailure("dev"); // fallo adicional mientras está abierto → re-extiende cooldown

        var (_, _, remaining) = cb.GetSnapshot();
        Assert.True(remaining >= 59); // cooldown re-arrancado desde ahora
    }

    // ── Half-Open: fallo de sonda ─────────────────────────────────────────────

    [Fact]
    public async Task HalfOpen_ProbeFails_ReOpensCooldown()
    {
        var cb = new CircuitBreaker(
            threshold: 2,
            cooldown: TimeSpan.FromMilliseconds(50),
            probeInterval: TimeSpan.FromMilliseconds(10));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        await Task.Delay(100); // expirar cooldown → half-open

        Assert.Null(cb.TryAcquire()); // sonda permitida
        cb.RecordFailure("dev");      // sonda falla → re-abre con nuevo cooldown

        // El circuito debe rechazar inmediatamente (cooldown reiniciado)
        Assert.NotNull(cb.TryAcquire());
    }

    [Fact]
    public async Task GetSnapshot_WhenHalfOpen_ReportsOpenWithZeroRemaining()
    {
        var cb = new CircuitBreaker(threshold: 2, cooldown: TimeSpan.FromMilliseconds(50));
        cb.RecordFailure("dev");
        cb.RecordFailure("dev");

        await Task.Delay(100); // cooldown expirado → half-open

        var (isOpen, _, remaining) = cb.GetSnapshot();
        Assert.True(isOpen);
        Assert.Equal(0, remaining);
    }
}
