// =============================================================================
// ModbusReadWrite Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates reading and writing data on Modbus TCP devices (port 502).
// Supports any device that speaks Modbus TCP protocol.
//
// Modbus TCP addressing:
//   HR100    - Holding Register 100 (read/write, 16-bit)
//   IR100    - Input Register 100 (read-only, 16-bit)
//   C100     - Coil 100 (read/write, 1-bit)
//   DI100    - Discrete Input 100 (read-only, 1-bit)
//
//   Classic Modbus numeric addresses:
//   400001   - Holding Register 0
//   300001   - Input Register 0
//   100001   - Discrete Input 0
//   1        - Coil 0
// =============================================================================

using SimplePLCDriverCore.Drivers;

// --- Connect to a Modbus TCP device ---
await using var device = PlcDriverFactory.CreateModbus("192.168.1.50");
await device.ConnectAsync();
Console.WriteLine("Connected to Modbus TCP device");

// =============================================================================
// Reading Holding Registers (HR) - Read/Write, 16-bit
// =============================================================================

var result = await device.ReadAsync("HR100");
if (result.IsSuccess)
{
    Console.WriteLine($"HR100 = {result.Value} ({result.TypeName})");
    // Output: HR100 = 42 (HOLDING_REGISTER)

    short regValue = result.Value;
    Console.WriteLine($"As short: {regValue}");
}
else
{
    Console.WriteLine($"Read failed: {result.Error}");
}

// Read using classic Modbus addressing (400001 = Holding Register 0)
var classicResult = await device.ReadAsync("400001");
Console.WriteLine($"400001 (HR0) = {(classicResult.IsSuccess ? classicResult.Value : classicResult.Error)}");

// =============================================================================
// Reading Input Registers (IR) - Read-Only, 16-bit
// =============================================================================

var irResult = await device.ReadAsync("IR0");
Console.WriteLine($"IR0 = {(irResult.IsSuccess ? irResult.Value : irResult.Error)}");

// Classic addressing: 300001 = Input Register 0
var irClassic = await device.ReadAsync("300001");
Console.WriteLine($"300001 (IR0) = {(irClassic.IsSuccess ? irClassic.Value : irClassic.Error)}");

// =============================================================================
// Reading Coils (C) - Read/Write, 1-bit
// =============================================================================

var coilResult = await device.ReadAsync("C0");
if (coilResult.IsSuccess)
{
    bool coilValue = coilResult.Value;
    Console.WriteLine($"C0 = {coilValue} ({coilResult.TypeName})");
}

// Classic addressing: 1 = Coil 0
var coilClassic = await device.ReadAsync("1");
Console.WriteLine($"1 (C0) = {(coilClassic.IsSuccess ? coilClassic.Value : coilClassic.Error)}");

// =============================================================================
// Reading Discrete Inputs (DI) - Read-Only, 1-bit
// =============================================================================

var diResult = await device.ReadAsync("DI0");
Console.WriteLine($"DI0 = {(diResult.IsSuccess ? diResult.Value : diResult.Error)}");

// Classic addressing: 100001 = Discrete Input 0
var diClassic = await device.ReadAsync("100001");
Console.WriteLine($"100001 (DI0) = {(diClassic.IsSuccess ? diClassic.Value : diClassic.Error)}");

// =============================================================================
// Writing Holding Registers
// =============================================================================

var writeResult = await device.WriteAsync("HR100", (short)42);
Console.WriteLine($"Write HR100: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

writeResult = await device.WriteAsync("HR101", (short)-100);
Console.WriteLine($"Write HR101: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// Writing Coils
// =============================================================================

writeResult = await device.WriteAsync("C0", true);
Console.WriteLine($"Write C0=true: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

writeResult = await device.WriteAsync("C1", false);
Console.WriteLine($"Write C1=false: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// Read-Only Protection
// =============================================================================

// Input Registers and Discrete Inputs are read-only
var roResult = await device.WriteAsync("IR0", (short)42);
Console.WriteLine($"Write IR0 (should fail): {roResult.Error}");

roResult = await device.WriteAsync("DI0", true);
Console.WriteLine($"Write DI0 (should fail): {roResult.Error}");

// =============================================================================
// Batch Read
// =============================================================================

var batchResults = await device.ReadAsync(new[] { "HR0", "HR1", "HR2", "C0", "IR0" });

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

var batchWriteResults = await device.WriteAsync(new[]
{
    ("HR0", (object)(short)10),
    ("HR1", (object)(short)20),
    ("HR2", (object)(short)30),
});

Console.WriteLine("\nBatch Write Results:");
foreach (var r in batchWriteResults)
{
    Console.WriteLine($"  {r.TagName}: {(r.IsSuccess ? "OK" : r.Error)}");
}

// =============================================================================
// Custom Port and Unit ID
// =============================================================================

// Connect to a device on a non-standard port with a specific unit/slave ID
await using var device2 = PlcDriverFactory.CreateModbus("192.168.1.51", port: 5020, unitId: 2);
// await device2.ConnectAsync();

// =============================================================================
// Error Handling
// =============================================================================

try
{
    var value = (await device.ReadAsync("HR100")).GetValueOrThrow();
    Console.WriteLine($"\nValue (exception style): {value}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\nDone!");
