// =============================================================================
// SlcReadWrite Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates reading and writing data on SLC 500, MicroLogix, and PLC-5
// controllers using PCCC (Programmable Controller Communication Commands)
// over CIP (Common Industrial Protocol).
//
// SLC addressing uses file-based notation:
//   N7:0   - Integer file 7, element 0
//   F8:1   - Float file 8, element 1
//   B3:0/5 - Bit file 3, element 0, bit 5
//   T4:0   - Timer file 4, element 0 (full structure)
//   T4:0.ACC - Timer accumulator sub-element
//   C5:0   - Counter file 5, element 0
//   ST9:0  - String file 9, element 0
//   L10:0  - Long integer file 10, element 0
// =============================================================================

using SimplePLCDriverCore.Drivers;

// --- Connect to an SLC 500 PLC ---
await using var plc = PlcDriverFactory.CreateSlc("192.168.1.50");
await plc.ConnectAsync();
Console.WriteLine("Connected to SLC 500");

// =============================================================================
// Reading Integer Values (N files)
// =============================================================================

var result = await plc.ReadAsync("N7:0");
if (result.IsSuccess)
{
    Console.WriteLine($"N7:0 = {result.Value} ({result.TypeName})");
    // Output: N7:0 = 42 (INT)

    short intValue = result.Value;
    Console.WriteLine($"As short: {intValue}");
}
else
{
    Console.WriteLine($"Read failed: {result.Error}");
}

// =============================================================================
// Reading Float Values (F files)
// =============================================================================

var floatResult = await plc.ReadAsync("F8:0");
if (floatResult.IsSuccess)
{
    float floatValue = floatResult.Value;
    Console.WriteLine($"F8:0 = {floatValue} ({floatResult.TypeName})");
    // Output: F8:0 = 3.14 (FLOAT)
}

// =============================================================================
// Reading Bit Values (B files)
// =============================================================================

// Read a single bit from a word
var bitResult = await plc.ReadAsync("B3:0/5");
if (bitResult.IsSuccess)
{
    bool bitValue = bitResult.Value;
    Console.WriteLine($"B3:0/5 = {bitValue} ({bitResult.TypeName})");
    // Output: B3:0/5 = True (BIT)
}

// =============================================================================
// Reading Timer Structures (T files)
// =============================================================================

// Read the full timer structure (control word + preset + accumulator)
var timerResult = await plc.ReadAsync("T4:0");
if (timerResult.IsSuccess)
{
    Console.WriteLine($"Timer T4:0 ({timerResult.TypeName}):");

    var members = timerResult.Value.AsStructure();
    if (members != null)
    {
        Console.WriteLine($"  PRE = {members["PRE"]}");
        Console.WriteLine($"  ACC = {members["ACC"]}");
        Console.WriteLine($"  EN  = {members["EN"]}");
        Console.WriteLine($"  TT  = {members["TT"]}");
        Console.WriteLine($"  DN  = {members["DN"]}");
    }
}

// Read just the accumulator sub-element
var accResult = await plc.ReadAsync("T4:0.ACC");
if (accResult.IsSuccess)
{
    Console.WriteLine($"T4:0.ACC = {accResult.Value}");
}

// =============================================================================
// Reading Counter Structures (C files)
// =============================================================================

var counterResult = await plc.ReadAsync("C5:0");
if (counterResult.IsSuccess)
{
    var members = counterResult.Value.AsStructure();
    if (members != null)
    {
        Console.WriteLine($"Counter C5:0:");
        Console.WriteLine($"  PRE = {members["PRE"]}");
        Console.WriteLine($"  ACC = {members["ACC"]}");
        Console.WriteLine($"  CU  = {members["CU"]}");
        Console.WriteLine($"  DN  = {members["DN"]}");
    }
}

// =============================================================================
// Reading String Values (ST files)
// =============================================================================

var strResult = await plc.ReadAsync("ST9:0");
if (strResult.IsSuccess)
{
    string text = strResult.Value;
    Console.WriteLine($"ST9:0 = \"{text}\" ({strResult.TypeName})");
}

// =============================================================================
// Reading Long Integer Values (L files)
// =============================================================================

var longResult = await plc.ReadAsync("L10:0");
if (longResult.IsSuccess)
{
    int longValue = longResult.Value;
    Console.WriteLine($"L10:0 = {longValue} ({longResult.TypeName})");
}

// =============================================================================
// Writing Values
// =============================================================================

// Write an integer
var writeResult = await plc.WriteAsync("N7:0", 100);
Console.WriteLine($"Write N7:0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a float
writeResult = await plc.WriteAsync("F8:0", 3.14f);
Console.WriteLine($"Write F8:0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a bit (uses read-modify-write internally to preserve other bits)
writeResult = await plc.WriteAsync("B3:0/5", true);
Console.WriteLine($"Write B3:0/5: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a string
writeResult = await plc.WriteAsync("ST9:0", "Hello SLC!");
Console.WriteLine($"Write ST9:0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a timer sub-element
writeResult = await plc.WriteAsync("T4:0.PRE", (short)5000);
Console.WriteLine($"Write T4:0.PRE: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// Batch Read (Sequential - PCCC does not support Multiple Service Packet)
// =============================================================================

var batchResults = await plc.ReadAsync(new[] { "N7:0", "N7:1", "F8:0", "B3:0/5" });

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
    ("N7:0", (object)42),
    ("N7:1", (object)99),
    ("F8:0", (object)2.718f),
});

Console.WriteLine("\nBatch Write Results:");
foreach (var r in batchWriteResults)
{
    Console.WriteLine($"  {r.TagName}: {(r.IsSuccess ? "OK" : r.Error)}");
}

// =============================================================================
// Using GetValueOrThrow for exception-based error handling
// =============================================================================

try
{
    var value = (await plc.ReadAsync("N7:0")).GetValueOrThrow();
    Console.WriteLine($"\nValue (exception style): {value}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

// =============================================================================
// MicroLogix and PLC-5 connections
// =============================================================================

// MicroLogix (same addressing, different internal handshaking)
// await using var mlx = PlcDriverFactory.CreateMicroLogix("192.168.1.60");
// await mlx.ConnectAsync();

// PLC-5 (same addressing, different internal handshaking)
// await using var plc5 = PlcDriverFactory.CreatePlc5("192.168.1.70");
// await plc5.ConnectAsync();

Console.WriteLine("\nDone!");
