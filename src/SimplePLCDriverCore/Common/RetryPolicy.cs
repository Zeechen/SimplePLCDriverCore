namespace SimplePLCDriverCore.Common;

/// <summary>
/// Defines a retry strategy for recoverable operations such as reconnection.
/// Supports fixed delay, exponential backoff, and optional jitter.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Maximum number of retry attempts before giving up.</summary>
    public int MaxAttempts { get; }

    /// <summary>Base delay between retries.</summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>Maximum delay cap when using exponential backoff.</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>Whether to use exponential backoff (delay doubles each attempt).</summary>
    public bool UseExponentialBackoff { get; }

    /// <summary>
    /// Whether to add random jitter (0-50% of calculated delay) to prevent
    /// thundering herd when multiple connections retry simultaneously.
    /// </summary>
    public bool UseJitter { get; }

    /// <summary>
    /// Optional predicate to determine if an exception is retryable.
    /// If null, all exceptions are considered retryable.
    /// </summary>
    public Func<Exception, bool>? IsRetryable { get; }

    public RetryPolicy(
        int maxAttempts,
        TimeSpan baseDelay,
        TimeSpan? maxDelay = null,
        bool useExponentialBackoff = false,
        bool useJitter = false,
        Func<Exception, bool>? isRetryable = null)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be at least 1.");
        if (baseDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "Must not be negative.");

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay;
        MaxDelay = maxDelay ?? TimeSpan.FromMinutes(1);
        UseExponentialBackoff = useExponentialBackoff;
        UseJitter = useJitter;
        IsRetryable = isRetryable;
    }

    /// <summary>
    /// Execute an async operation with retry logic.
    /// </summary>
    /// <param name="operation">The async operation to execute. Receives the attempt number (1-based).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <returns>The result of the first successful attempt.</returns>
    /// <exception cref="RetryExhaustedException">Thrown when all attempts are exhausted.</exception>
    public async ValueTask<T> ExecuteAsync<T>(
        Func<int, CancellationToken, ValueTask<T>> operation,
        CancellationToken ct = default)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation(attempt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Don't retry on user cancellation
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (IsRetryable != null && !IsRetryable(ex))
                    throw;

                if (attempt == MaxAttempts)
                    break;

                var delay = CalculateDelay(attempt);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new RetryExhaustedException(MaxAttempts, lastException!);
    }

    /// <summary>
    /// Execute an async operation with retry logic (no return value).
    /// </summary>
    public async ValueTask ExecuteAsync(
        Func<int, CancellationToken, ValueTask> operation,
        CancellationToken ct = default)
    {
        await ExecuteAsync<bool>(async (attempt, token) =>
        {
            await operation(attempt, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculate the delay for a given attempt number.
    /// Visible for testing.
    /// </summary>
    internal TimeSpan CalculateDelay(int attempt)
    {
        var delay = BaseDelay;

        if (UseExponentialBackoff && attempt > 1)
        {
            // 2^(attempt-1) * base, capped at MaxDelay
            var multiplier = Math.Pow(2, attempt - 1);
            var ticks = (long)(BaseDelay.Ticks * multiplier);
            delay = ticks > MaxDelay.Ticks
                ? MaxDelay
                : TimeSpan.FromTicks(ticks);
        }

        if (UseJitter)
        {
            // Add 0-50% random jitter
            var jitterFraction = Random.Shared.NextDouble() * 0.5;
            delay += TimeSpan.FromTicks((long)(delay.Ticks * jitterFraction));

            if (delay > MaxDelay)
                delay = MaxDelay;
        }

        return delay;
    }

    // --- Factory Methods ---

    /// <summary>
    /// Create a policy with fixed delay between retries.
    /// </summary>
    public static RetryPolicy FixedDelay(int maxAttempts, TimeSpan delay)
        => new(maxAttempts, delay);

    /// <summary>
    /// Create a policy with exponential backoff.
    /// </summary>
    public static RetryPolicy ExponentialBackoff(
        int maxAttempts, TimeSpan baseDelay, TimeSpan? maxDelay = null, bool useJitter = true)
        => new(maxAttempts, baseDelay, maxDelay, useExponentialBackoff: true, useJitter: useJitter);

    /// <summary>
    /// Create the default reconnection policy used by ConnectionManager.
    /// 3 attempts with exponential backoff starting at 2s, jitter enabled.
    /// </summary>
    public static RetryPolicy DefaultReconnect()
        => ExponentialBackoff(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), useJitter: true);

    /// <summary>
    /// Create a policy for IO-retryable operations only (IOException, SocketException).
    /// </summary>
    public static RetryPolicy IoRetry(int maxAttempts = 2, TimeSpan? baseDelay = null)
        => new(maxAttempts, baseDelay ?? TimeSpan.FromMilliseconds(500),
            isRetryable: ex => ex is IOException or System.Net.Sockets.SocketException);
}

/// <summary>
/// Thrown when all retry attempts are exhausted.
/// </summary>
public sealed class RetryExhaustedException : Exception
{
    public int AttemptsExhausted { get; }

    public RetryExhaustedException(int attempts, Exception lastException)
        : base($"Operation failed after {attempts} attempts.", lastException)
    {
        AttemptsExhausted = attempts;
    }
}
