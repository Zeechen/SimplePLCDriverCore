// =============================================================================
// ConnectionManagement Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates connection options, retry policies, connection pooling,
// cancellation, and proper resource management patterns.
// =============================================================================

using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Drivers;

// =============================================================================
// 1. Basic Connection (CompactLogix - slot 0)
// =============================================================================

Console.WriteLine("--- Basic Connection ---\n");

await using (var plc = PlcDriverFactory.CreateCompactLogix("192.168.1.100"))
{
    await plc.ConnectAsync();
    Console.WriteLine($"Connected: {plc.IsConnected}");

    var result = await plc.ReadAsync("SomeTag");
    Console.WriteLine($"Read: {result.Value}");

    // Connection is automatically closed by DisposeAsync (via 'await using')
}
Console.WriteLine("Disconnected (auto-disposed)\n");

// =============================================================================
// 2. ControlLogix with Specific Slot
// =============================================================================

Console.WriteLine("--- ControlLogix (Slot 2) ---\n");

await using (var plc = PlcDriverFactory.CreateControlLogix("192.168.1.100", slot: 2))
{
    await plc.ConnectAsync();
    Console.WriteLine($"Connected to slot 2: {plc.IsConnected}");
}

// =============================================================================
// 3. Custom Connection Options
// =============================================================================

Console.WriteLine("--- Custom Options ---\n");

var options = new ConnectionOptions
{
    // TCP connect timeout
    ConnectTimeout = TimeSpan.FromSeconds(10),

    // Individual CIP request timeout
    RequestTimeout = TimeSpan.FromSeconds(15),

    // Keepalive interval (prevents CIP connection timeout)
    // Set to TimeSpan.Zero to disable keepalive
    KeepAliveInterval = TimeSpan.FromSeconds(20),

    // Auto-reconnect on connection loss
    AutoReconnect = true,
    MaxReconnectAttempts = 5,
    ReconnectDelay = TimeSpan.FromSeconds(3),

    // Use exponential backoff with jitter for reconnection instead of fixed delay.
    // This overrides MaxReconnectAttempts/ReconnectDelay when set.
    ReconnectPolicy = RetryPolicy.ExponentialBackoff(
        maxAttempts: 5,
        baseDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromSeconds(30)),

    // Processor slot (0 for CompactLogix)
    Slot = 0,
};

await using (var plc = PlcDriverFactory.CreateLogix("192.168.1.100", options: options))
{
    await plc.ConnectAsync();
    Console.WriteLine($"Connected with custom options: {plc.IsConnected}");
}

// =============================================================================
// 4. Cancellation Token Support
// =============================================================================

Console.WriteLine("\n--- Cancellation ---\n");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100");
    await plc.ConnectAsync(cts.Token);

    // Read with cancellation
    var result = await plc.ReadAsync("SomeTag", cts.Token);
    Console.WriteLine($"Read: {result.Value}");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was cancelled (timeout)");
}

// =============================================================================
// 5. Manual Connect/Disconnect
// =============================================================================

Console.WriteLine("\n--- Manual Lifecycle ---\n");

var manualPlc = PlcDriverFactory.CreateLogix("192.168.1.100");

try
{
    await manualPlc.ConnectAsync();
    Console.WriteLine($"Connected: {manualPlc.IsConnected}");

    // Do work...
    var tagResult = await manualPlc.ReadAsync("Counter");
    Console.WriteLine($"Counter: {tagResult.Value}");

    // Explicit disconnect
    await manualPlc.DisconnectAsync();
    Console.WriteLine($"Connected after disconnect: {manualPlc.IsConnected}");

    // Can reconnect after disconnect
    await manualPlc.ConnectAsync();
    Console.WriteLine($"Reconnected: {manualPlc.IsConnected}");
}
finally
{
    await manualPlc.DisposeAsync();
    Console.WriteLine("Disposed");
}

// =============================================================================
// 6. Error Handling Pattern
// =============================================================================

Console.WriteLine("\n--- Error Handling ---\n");

try
{
    await using var plc = PlcDriverFactory.CreateLogix("10.0.0.1"); // Unreachable
    await plc.ConnectAsync();
}
catch (IOException ex)
{
    Console.WriteLine($"Connection failed (expected): {ex.Message}");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Connection timed out (expected): {ex.Message}");
}

// =============================================================================
// 7. Long-Running Polling Pattern
// =============================================================================

Console.WriteLine("\n--- Polling Pattern ---\n");

await using (var plc = PlcDriverFactory.CreateLogix("192.168.1.100"))
{
    await plc.ConnectAsync();

    using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    try
    {
        while (!pollCts.Token.IsCancellationRequested)
        {
            var readings = await plc.ReadAsync(
                new[] { "Temperature", "Pressure", "Level" },
                pollCts.Token);

            foreach (var reading in readings)
            {
                if (reading.IsSuccess)
                    Console.Write($"{reading.TagName}={reading.Value:F1}  ");
            }
            Console.WriteLine();

            await Task.Delay(1000, pollCts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Polling stopped");
    }
}

// =============================================================================
// 8. Retry Policy - Custom Strategies
// =============================================================================

Console.WriteLine("\n--- Retry Policies ---\n");

// Fixed delay: wait 2 seconds between each of 3 attempts
var fixedPolicy = RetryPolicy.FixedDelay(maxAttempts: 3, delay: TimeSpan.FromSeconds(2));
Console.WriteLine($"Fixed: {fixedPolicy.MaxAttempts} attempts, {fixedPolicy.BaseDelay.TotalSeconds}s delay");

// Exponential backoff: 1s -> 2s -> 4s -> 8s (with jitter to prevent thundering herd)
var expPolicy = RetryPolicy.ExponentialBackoff(
    maxAttempts: 4,
    baseDelay: TimeSpan.FromSeconds(1),
    maxDelay: TimeSpan.FromSeconds(30),
    useJitter: true);
Console.WriteLine($"Exponential: {expPolicy.MaxAttempts} attempts, backoff={expPolicy.UseExponentialBackoff}, jitter={expPolicy.UseJitter}");

// IO-only retry: only retries IOException/SocketException, ignores others
var ioPolicy = RetryPolicy.IoRetry(maxAttempts: 2);
Console.WriteLine($"IO-only: {ioPolicy.MaxAttempts} attempts, filters non-IO exceptions");

// Default reconnect: used by ConnectionManager when no explicit policy is set
var defaultPolicy = RetryPolicy.DefaultReconnect();
Console.WriteLine($"Default: {defaultPolicy.MaxAttempts} attempts, base={defaultPolicy.BaseDelay.TotalSeconds}s");

// Apply a policy to connection options
var retryOptions = new ConnectionOptions
{
    AutoReconnect = true,
    ReconnectPolicy = RetryPolicy.ExponentialBackoff(5, TimeSpan.FromSeconds(2)),
};
Console.WriteLine($"\nOptions with exponential backoff configured");

// =============================================================================
// 9. Connection Pool - Multi-PLC Management
// =============================================================================

Console.WriteLine("\n--- Connection Pool ---\n");

await using (var pool = new ConnectionPool())
{
    // Register multiple PLCs by name
    pool.Register("Line1_PLC", "192.168.1.100");
    pool.Register("Line2_PLC", "192.168.1.101", slot: 2);
    pool.Register("Packaging", "192.168.1.102", options: new ConnectionOptions
    {
        RequestTimeout = TimeSpan.FromSeconds(20),
        ReconnectPolicy = RetryPolicy.ExponentialBackoff(5, TimeSpan.FromSeconds(1)),
    });

    Console.WriteLine($"Registered: {string.Join(", ", pool.RegisteredNames)}");
    Console.WriteLine($"Connected: {string.Join(", ", pool.ConnectedNames)}"); // empty - lazy connect

    // Get a driver by name - connects lazily on first access
    try
    {
        var line1 = await pool.GetAsync("Line1_PLC");
        Console.WriteLine($"Line1 connected: {line1.IsConnected}");

        var result = await line1.ReadAsync("ProductCount");
        Console.WriteLine($"ProductCount = {result.Value}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Line1 connection failed (expected in demo): {ex.Message}");
    }

    // Check connection status
    Console.WriteLine($"Line1 connected: {pool.IsConnected("Line1_PLC")}");
    Console.WriteLine($"Line2 connected: {pool.IsConnected("Line2_PLC")}");

    // Disconnect a specific PLC (registration preserved for reconnect)
    await pool.DisconnectAsync("Line1_PLC");

    // Unregister removes the registration entirely
    await pool.UnregisterAsync("Packaging");
    Console.WriteLine($"After unregister: {string.Join(", ", pool.RegisteredNames)}");

    // Disconnect all active connections at once
    await pool.DisconnectAllAsync();

    // Pool.DisposeAsync() is called automatically by 'await using'
}

Console.WriteLine("\nDone!");
