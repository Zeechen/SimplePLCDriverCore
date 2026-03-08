// =============================================================================
// S7ReadWrite Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates reading and writing data on Siemens S7-300, S7-400, S7-1200,
// and S7-1500 PLCs using the S7comm protocol over ISO-on-TCP (port 102).
//
// S7 addressing uses the standard notation:
//   DB1.DBX0.0   - Data Block 1, bit at byte 0, bit 0
//   DB1.DBB0     - Data Block 1, byte at byte 0
//   DB1.DBW0     - Data Block 1, word (INT) at byte 0
//   DB1.DBD0     - Data Block 1, double word (DINT/REAL) at byte 0
//   DB1.DBS0.20  - Data Block 1, string at byte 0, max length 20
//   I0.0         - Input bit 0.0
//   IB0, IW0, ID0 - Input byte/word/dword
//   Q0.0         - Output bit 0.0
//   M0.0         - Merker bit 0.0
//   MB0, MW0, MD0 - Merker byte/word/dword
//   T0, C0       - Timer 0, Counter 0
// =============================================================================

using SimplePLCDriverCore.Drivers;

// --- Connect to an S7-1200 PLC (rack 0, slot 0) ---
await using var plc = PlcDriverFactory.CreateS7_1200("192.168.1.200");
await plc.ConnectAsync();
Console.WriteLine("Connected to S7-1200");

// For S7-300/400 (rack 0, slot 2):
// await using var plc300 = PlcDriverFactory.CreateS7_300("192.168.1.100");

// For custom rack/slot:
// await using var plc = PlcDriverFactory.CreateSiemens("192.168.1.200", rack: 0, slot: 2);

// =============================================================================
// Reading Data Block Values
// =============================================================================

// Read a word (INT, 16-bit)
var result = await plc.ReadAsync("DB1.DBW0");
if (result.IsSuccess)
{
    Console.WriteLine($"DB1.DBW0 = {result.Value} ({result.TypeName})");
    // Output: DB1.DBW0 = 42 (WORD)

    short intValue = result.Value;
    Console.WriteLine($"As short: {intValue}");
}
else
{
    Console.WriteLine($"Read failed: {result.Error}");
}

// Read a double word (DINT, 32-bit)
var dwordResult = await plc.ReadAsync("DB1.DBD4");
if (dwordResult.IsSuccess)
{
    int dintValue = dwordResult.Value;
    Console.WriteLine($"DB1.DBD4 = {dintValue} ({dwordResult.TypeName})");
}

// Read a REAL (32-bit float)
var realResult = await plc.ReadAsync("DB1.DBD8");
if (realResult.IsSuccess)
{
    float realValue = realResult.Value;
    Console.WriteLine($"DB1.DBD8 = {realValue} ({realResult.TypeName})");
}

// Read a single bit
var bitResult = await plc.ReadAsync("DB1.DBX12.0");
if (bitResult.IsSuccess)
{
    bool flag = bitResult.Value;
    Console.WriteLine($"DB1.DBX12.0 = {flag} ({bitResult.TypeName})");
}

// Read a byte
var byteResult = await plc.ReadAsync("DB1.DBB13");
if (byteResult.IsSuccess)
{
    Console.WriteLine($"DB1.DBB13 = {byteResult.Value} ({byteResult.TypeName})");
}

// Read a string (S7 format: max length header + actual length + chars)
var strResult = await plc.ReadAsync("DB1.DBS14.20");
if (strResult.IsSuccess)
{
    string text = strResult.Value;
    Console.WriteLine($"DB1.DBS14.20 = \"{text}\" ({strResult.TypeName})");
}

// =============================================================================
// Reading Process I/O and Merker Areas
// =============================================================================

// Read input bit
var inputBit = await plc.ReadAsync("I0.0");
Console.WriteLine($"I0.0 = {(inputBit.IsSuccess ? inputBit.Value : inputBit.Error)}");

// Read output word
var outputWord = await plc.ReadAsync("QW0");
Console.WriteLine($"QW0 = {(outputWord.IsSuccess ? outputWord.Value : outputWord.Error)}");

// Read merker byte
var merkerByte = await plc.ReadAsync("MB0");
Console.WriteLine($"MB0 = {(merkerByte.IsSuccess ? merkerByte.Value : merkerByte.Error)}");

// Read merker double word
var merkerDWord = await plc.ReadAsync("MD4");
Console.WriteLine($"MD4 = {(merkerDWord.IsSuccess ? merkerDWord.Value : merkerDWord.Error)}");

// =============================================================================
// Reading Timers and Counters
// =============================================================================

var timerResult = await plc.ReadAsync("T0");
Console.WriteLine($"T0 = {(timerResult.IsSuccess ? timerResult.Value : timerResult.Error)}");

var counterResult = await plc.ReadAsync("C0");
Console.WriteLine($"C0 = {(counterResult.IsSuccess ? counterResult.Value : counterResult.Error)}");

// =============================================================================
// Writing Values
// =============================================================================

// Write a word
var writeResult = await plc.WriteAsync("DB1.DBW0", (short)42);
Console.WriteLine($"Write DB1.DBW0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a double word
writeResult = await plc.WriteAsync("DB1.DBD4", 100000);
Console.WriteLine($"Write DB1.DBD4: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a REAL
writeResult = await plc.WriteAsync("DB1.DBD8", 3.14f);
Console.WriteLine($"Write DB1.DBD8: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a bit
writeResult = await plc.WriteAsync("DB1.DBX12.0", true);
Console.WriteLine($"Write DB1.DBX12.0: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a byte
writeResult = await plc.WriteAsync("DB1.DBB13", (byte)0xFF);
Console.WriteLine($"Write DB1.DBB13: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// Batch Read (S7 Multi-Item Read - up to 20 items per request)
// =============================================================================

var batchResults = await plc.ReadAsync(new[]
{
    "DB1.DBW0", "DB1.DBD4", "DB1.DBX12.0", "MB0"
});

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
    ("DB1.DBW0", (object)(short)42),
    ("DB1.DBD4", (object)100000),
    ("DB1.DBX12.0", (object)true),
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
    var value = (await plc.ReadAsync("DB1.DBW0")).GetValueOrThrow();
    Console.WriteLine($"\nValue (exception style): {value}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\nDone!");
