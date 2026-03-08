// =============================================================================
// FinsReadWrite Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates reading and writing data on Omron PLCs using FINS (Factory
// Interface Network Service) over TCP (port 9600).
// Supports Omron NJ, NX, CJ, CP, and CS series PLCs.
//
// FINS addressing uses Omron memory area notation:
//   D0       - DM area, word 0
//   D100     - DM area, word 100
//   D100.0   - DM area, word 100, bit 0
//   CIO0     - CIO area, word 0
//   CIO0.00  - CIO area, word 0, bit 0
//   W0       - Work area, word 0
//   W0.05    - Work area, word 0, bit 5
//   H0       - Holding area, word 0
//   A0       - Auxiliary area, word 0
//   T0       - Timer PV, number 0
//   C0       - Counter PV, number 0
//   E0_0     - Extended memory bank 0, word 0
// =============================================================================

using SimplePLCDriverCore.Drivers;

// --- Connect to an Omron PLC ---
await using var plc = PlcDriverFactory.CreateOmron("192.168.1.100");
await plc.ConnectAsync();
Console.WriteLine("Connected to Omron PLC");

// =============================================================================
// Reading DM (Data Memory) Area
// =============================================================================

// Read a word from DM area
var result = await plc.ReadAsync("D100");
if (result.IsSuccess)
{
    Console.WriteLine($"D100 = {result.Value} ({result.TypeName})");
    // Output: D100 = 42 (WORD)

    short wordValue = result.Value;
    Console.WriteLine($"As short: {wordValue}");
}
else
{
    Console.WriteLine($"Read failed: {result.Error}");
}

// Read a bit from DM area
var bitResult = await plc.ReadAsync("D100.5");
if (bitResult.IsSuccess)
{
    bool bitValue = bitResult.Value;
    Console.WriteLine($"D100.5 = {bitValue} ({bitResult.TypeName})");
}

// =============================================================================
// Reading CIO (Core I/O) Area
// =============================================================================

var cioResult = await plc.ReadAsync("CIO0");
Console.WriteLine($"CIO0 = {(cioResult.IsSuccess ? cioResult.Value : cioResult.Error)}");

// Read a specific CIO bit
var cioBit = await plc.ReadAsync("CIO0.00");
Console.WriteLine($"CIO0.00 = {(cioBit.IsSuccess ? cioBit.Value : cioBit.Error)}");

// =============================================================================
// Reading Other Areas
// =============================================================================

// Work area
var workResult = await plc.ReadAsync("W0");
Console.WriteLine($"W0 = {(workResult.IsSuccess ? workResult.Value : workResult.Error)}");

// Holding area
var holdResult = await plc.ReadAsync("H0");
Console.WriteLine($"H0 = {(holdResult.IsSuccess ? holdResult.Value : holdResult.Error)}");

// Auxiliary area
var auxResult = await plc.ReadAsync("A0");
Console.WriteLine($"A0 = {(auxResult.IsSuccess ? auxResult.Value : auxResult.Error)}");

// Timer present value
var timerResult = await plc.ReadAsync("T0");
Console.WriteLine($"T0 = {(timerResult.IsSuccess ? timerResult.Value : timerResult.Error)}");

// Counter present value
var counterResult = await plc.ReadAsync("C0");
Console.WriteLine($"C0 = {(counterResult.IsSuccess ? counterResult.Value : counterResult.Error)}");

// =============================================================================
// Writing Values
// =============================================================================

// Write a word to DM area
var writeResult = await plc.WriteAsync("D100", (short)42);
Console.WriteLine($"Write D100: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a word to CIO area
writeResult = await plc.WriteAsync("CIO0", (short)0x00FF);
Console.WriteLine($"Write CIO0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a bit
writeResult = await plc.WriteAsync("CIO0.00", true);
Console.WriteLine($"Write CIO0.00: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write to Work area
writeResult = await plc.WriteAsync("W0", (short)100);
Console.WriteLine($"Write W0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write to Holding area
writeResult = await plc.WriteAsync("H0", (short)500);
Console.WriteLine($"Write H0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// Batch Read (Sequential - one FINS command per address)
// =============================================================================

var batchResults = await plc.ReadAsync(new[] { "D100", "D101", "D102", "CIO0", "W0" });

Console.WriteLine("\nBatch Read Results:");
foreach (var r in batchResults)
{
    if (r.IsSuccess)
        Console.WriteLine($"  {r.TagName} = {r.Value} ({r.TypeName})");
    else
        Console.WriteLine($"  {r.TagName} ERROR: {r.Error}");
}

// =============================================================================
// Batch Write
// =============================================================================

var batchWriteResults = await plc.WriteAsync(new[]
{
    ("D100", (object)(short)10),
    ("D101", (object)(short)20),
    ("D102", (object)(short)30),
});

Console.WriteLine("\nBatch Write Results:");
foreach (var r in batchWriteResults)
{
    Console.WriteLine($"  {r.TagName}: {(r.IsSuccess ? "OK" : r.Error)}");
}

// =============================================================================
// Error Handling
// =============================================================================

try
{
    var value = (await plc.ReadAsync("D100")).GetValueOrThrow();
    Console.WriteLine($"\nValue (exception style): {value}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\nDone!");
