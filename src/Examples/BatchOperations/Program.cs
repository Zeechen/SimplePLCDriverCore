// =============================================================================
// BatchOperations Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates high-performance batch read/write operations.
// Multiple tags are packed into CIP Multiple Service Packets, minimizing
// network round-trips. 100 tags can be read in 1-3 round-trips instead of 100.
// =============================================================================

using SimplePLCDriverCore.Drivers;

await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100");
await plc.ConnectAsync();
Console.WriteLine("Connected to PLC\n");

// =============================================================================
// Batch Read - Multiple tags in minimal round-trips
// =============================================================================

Console.WriteLine("--- Batch Read ---");

var tagNames = new[]
{
    "Temperature",
    "Pressure",
    "FlowRate",
    "Level",
    "Speed",
    "Setpoint",
    "Output",
    "AlarmHigh",
    "AlarmLow",
    "RunStatus",
};

// All tags are read using CIP Multiple Service Packet (service 0x0A)
// The driver automatically packs them into the minimum number of packets
var results = await plc.ReadAsync(tagNames);

Console.WriteLine($"Read {results.Length} tags:\n");
foreach (var result in results)
{
    if (result.IsSuccess)
    {
        Console.WriteLine($"  {result.TagName,-15} = {result.Value,-12} ({result.TypeName})");
    }
    else
    {
        Console.WriteLine($"  {result.TagName,-15} = ERROR: {result.Error}");
    }
}

// =============================================================================
// Batch Write - Multiple tags in minimal round-trips
// =============================================================================

Console.WriteLine("\n--- Batch Write ---");

var writeData = new (string TagName, object Value)[]
{
    ("Setpoint", 75.5f),
    ("Speed", 1200),
    ("Output", 50.0f),
    ("AlarmHigh", 100.0f),
    ("AlarmLow", 10.0f),
    ("RunStatus", true),
};

var writeResults = await plc.WriteAsync(writeData);

Console.WriteLine($"Wrote {writeResults.Length} tags:\n");
foreach (var result in writeResults)
{
    var status = result.IsSuccess ? "OK" : $"ERROR: {result.Error}";
    Console.WriteLine($"  {result.TagName,-15} -> {status}");
}

// =============================================================================
// Large Batch - Auto-splitting across multiple packets
// =============================================================================
// When the total request size exceeds the CIP connection size (4002 bytes
// with Large Forward Open), the driver automatically splits into multiple
// network round-trips. This is transparent to the caller.

Console.WriteLine("\n--- Large Batch (Auto-Split) ---");

// Generate a list of 50 tag names
var manyTags = Enumerable.Range(0, 50)
    .Select(i => $"DataPoint[{i}]")
    .ToArray();

var largeResults = await plc.ReadAsync(manyTags);

var successCount = largeResults.Count(r => r.IsSuccess);
var failCount = largeResults.Count(r => !r.IsSuccess);
Console.WriteLine($"Read {manyTags.Length} tags: {successCount} succeeded, {failCount} failed");

// =============================================================================
// Mixed Read/Write Pattern - Typical control loop
// =============================================================================

Console.WriteLine("\n--- Control Loop Pattern ---");

// Read current state
var stateResults = await plc.ReadAsync(new[] { "ProcessValue", "Setpoint", "RunStatus" });

if (stateResults.All(r => r.IsSuccess))
{
    float processValue = stateResults[0].Value;
    float setpoint = stateResults[1].Value;
    bool running = stateResults[2].Value;

    Console.WriteLine($"PV={processValue}, SP={setpoint}, Running={running}");

    // Calculate new output (simplified PID)
    if (running)
    {
        var error = setpoint - processValue;
        var newOutput = Math.Clamp(error * 0.5f, 0f, 100f);

        // Write output
        var outputResult = await plc.WriteAsync("Output", newOutput);
        Console.WriteLine($"Output set to {newOutput:F1}%: {(outputResult.IsSuccess ? "OK" : outputResult.Error)}");
    }
}

// =============================================================================
// Error Handling in Batch - Individual failures don't affect other tags
// =============================================================================

Console.WriteLine("\n--- Error Handling ---");

var mixedResults = await plc.ReadAsync(new[] { "ValidTag", "NonExistentTag", "AnotherValidTag" });

foreach (var result in mixedResults)
{
    if (result.IsSuccess)
        Console.WriteLine($"  {result.TagName}: {result.Value}");
    else
        Console.WriteLine($"  {result.TagName}: FAILED - {result.Error}");
}

// =============================================================================
// Batch Read with UDT Member Dot Notation
// =============================================================================
// You can mix scalar tags and UDT member paths in a single batch read.
// The driver resolves each tag path independently.

Console.WriteLine("\n--- Batch Read: UDT Members (Dot Notation) ---");

var mixedTagNames = new[]
{
    "Temperature",                  // Scalar tag
    "Motor1.Speed",                 // UDT member
    "Motor1.Current",               // Another member of the same UDT
    "Motor2.Speed",                 // Member from a different UDT instance
    "Reactor.Setpoint",             // Nested UDT member
    "Reactor.PID.Output",           // Deeply nested member
    "AlarmStatus",                  // Scalar tag
};

var dotResults = await plc.ReadAsync(mixedTagNames);

Console.WriteLine($"Read {dotResults.Length} tags (mixed scalar + UDT members):\n");
foreach (var r in dotResults)
{
    if (r.IsSuccess)
        Console.WriteLine($"  {r.TagName,-25} = {r.Value,-12} ({r.TypeName})");
    else
        Console.WriteLine($"  {r.TagName,-25} = ERROR: {r.Error}");
}

// =============================================================================
// Batch Write with UDT Member Dot Notation
// =============================================================================
// Write to multiple UDT members from different tags in a single batch.
// The driver resolves each member's type from the UDT definition.

Console.WriteLine("\n--- Batch Write: UDT Members (Dot Notation) ---");

var dotWriteData = new (string TagName, object Value)[]
{
    ("Motor1.Speed", 1500),             // DINT member of Motor1
    ("Motor1.Enabled", true),           // BOOL member of Motor1
    ("Motor2.Speed", 1200),             // DINT member of Motor2
    ("Reactor.Setpoint", 75.5f),        // REAL member of Reactor
    ("Reactor.PID.Kp", 1.2f),          // Nested UDT member
    ("AlarmHigh", 100.0f),              // Scalar tag in the same batch
};

var dotWriteResults = await plc.WriteAsync(dotWriteData);

Console.WriteLine($"Wrote {dotWriteResults.Length} tags:\n");
foreach (var r in dotWriteResults)
{
    var status = r.IsSuccess ? "OK" : $"ERROR: {r.Error}";
    Console.WriteLine($"  {r.TagName,-25} -> {status}");
}

Console.WriteLine("\nDone!");
