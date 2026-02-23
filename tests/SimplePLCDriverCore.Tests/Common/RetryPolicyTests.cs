using SimplePLCDriverCore.Common;

namespace SimplePLCDriverCore.Tests.Common;

public class RetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_SucceedsFirstAttempt_ReturnsValue()
    {
        var policy = RetryPolicy.FixedDelay(3, TimeSpan.FromMilliseconds(10));

        var result = await policy.ExecuteAsync<int>(
            (attempt, ct) => new ValueTask<int>(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsAfterRetries_ReturnsValue()
    {
        var policy = RetryPolicy.FixedDelay(3, TimeSpan.FromMilliseconds(10));
        var attempts = 0;

        var result = await policy.ExecuteAsync<int>((attempt, ct) =>
        {
            attempts++;
            if (attempts < 3)
                throw new IOException("transient");
            return new ValueTask<int>(99);
        });

        Assert.Equal(99, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_AllAttemptsFail_ThrowsRetryExhausted()
    {
        var policy = RetryPolicy.FixedDelay(2, TimeSpan.FromMilliseconds(10));

        var ex = await Assert.ThrowsAsync<RetryExhaustedException>(() =>
            policy.ExecuteAsync<int>((attempt, ct) =>
                throw new IOException("always fails")).AsTask());

        Assert.Equal(2, ex.AttemptsExhausted);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOverload_Works()
    {
        var policy = RetryPolicy.FixedDelay(3, TimeSpan.FromMilliseconds(10));
        var callCount = 0;

        await policy.ExecuteAsync((attempt, ct) =>
        {
            callCount++;
            if (callCount < 2)
                throw new IOException("transient");
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationToken_StopsRetry()
    {
        var policy = RetryPolicy.FixedDelay(10, TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync<int>((attempt, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<int>(42);
            }, cts.Token).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableException_ThrowsImmediately()
    {
        var policy = new RetryPolicy(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10),
            isRetryable: ex => ex is IOException);

        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>((attempt, ct) =>
            {
                attempts++;
                throw new InvalidOperationException("not retryable");
            }).AsTask());

        Assert.Equal(1, attempts); // Should not retry
    }

    [Fact]
    public async Task ExecuteAsync_RetryableException_Retries()
    {
        var policy = new RetryPolicy(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10),
            isRetryable: ex => ex is IOException);

        var attempts = 0;

        var result = await policy.ExecuteAsync<int>((attempt, ct) =>
        {
            attempts++;
            if (attempts < 3)
                throw new IOException("transient");
            return new ValueTask<int>(77);
        });

        Assert.Equal(77, result);
        Assert.Equal(3, attempts);
    }

    // --- Delay Calculation Tests ---

    [Fact]
    public void CalculateDelay_FixedDelay_ReturnsSameValue()
    {
        var policy = RetryPolicy.FixedDelay(3, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), policy.CalculateDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.CalculateDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.CalculateDelay(3));
    }

    [Fact]
    public void CalculateDelay_ExponentialBackoff_Doubles()
    {
        var policy = new RetryPolicy(
            maxAttempts: 5,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromMinutes(1),
            useExponentialBackoff: true,
            useJitter: false);

        Assert.Equal(TimeSpan.FromSeconds(1), policy.CalculateDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.CalculateDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.CalculateDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.CalculateDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(16), policy.CalculateDelay(5));
    }

    [Fact]
    public void CalculateDelay_ExponentialBackoff_CappedAtMaxDelay()
    {
        var policy = new RetryPolicy(
            maxAttempts: 10,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(10),
            useExponentialBackoff: true,
            useJitter: false);

        // 2^5 = 32s > 10s max
        Assert.Equal(TimeSpan.FromSeconds(10), policy.CalculateDelay(6));
    }

    [Fact]
    public void CalculateDelay_WithJitter_AddsRandomComponent()
    {
        var policy = new RetryPolicy(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromSeconds(2),
            useJitter: true);

        // With jitter, delay should be between base and base * 1.5
        var delays = Enumerable.Range(0, 100).Select(_ => policy.CalculateDelay(1)).ToList();

        Assert.All(delays, d =>
        {
            Assert.True(d >= TimeSpan.FromSeconds(2), $"Delay {d} below base");
            Assert.True(d <= TimeSpan.FromSeconds(3), $"Delay {d} above max jitter");
        });

        // At least some should differ (jitter is random)
        Assert.True(delays.Distinct().Count() > 1, "Jitter should produce varying delays");
    }

    // --- Factory Method Tests ---

    [Fact]
    public void FixedDelay_CreatesCorrectPolicy()
    {
        var policy = RetryPolicy.FixedDelay(5, TimeSpan.FromSeconds(3));

        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(3), policy.BaseDelay);
        Assert.False(policy.UseExponentialBackoff);
        Assert.False(policy.UseJitter);
    }

    [Fact]
    public void ExponentialBackoff_CreatesCorrectPolicy()
    {
        var policy = RetryPolicy.ExponentialBackoff(
            4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        Assert.Equal(4, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
        Assert.True(policy.UseExponentialBackoff);
        Assert.True(policy.UseJitter);
    }

    [Fact]
    public void DefaultReconnect_HasReasonableDefaults()
    {
        var policy = RetryPolicy.DefaultReconnect();

        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.BaseDelay);
        Assert.True(policy.UseExponentialBackoff);
        Assert.True(policy.UseJitter);
    }

    [Fact]
    public void IoRetry_OnlyRetriesIoExceptions()
    {
        var policy = RetryPolicy.IoRetry();

        Assert.Equal(2, policy.MaxAttempts);
        Assert.NotNull(policy.IsRetryable);
        Assert.True(policy.IsRetryable!(new IOException()));
        Assert.True(policy.IsRetryable!(new System.Net.Sockets.SocketException()));
        Assert.False(policy.IsRetryable!(new InvalidOperationException()));
    }

    // --- Validation Tests ---

    [Fact]
    public void Constructor_ZeroMaxAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryPolicy(0, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Constructor_NegativeDelay_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryPolicy(3, TimeSpan.FromSeconds(-1)));
    }
}
