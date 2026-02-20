// =============================================================================
// ConnectionManagement Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates connection options, reconnection, cancellation,
// and proper resource management patterns.
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

Console.WriteLine("\nDone!");
