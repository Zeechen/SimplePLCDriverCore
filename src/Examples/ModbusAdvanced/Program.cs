// =============================================================================
// ModbusAdvanced Example - SimplePLCDriverCore Phase 5
// =============================================================================
// Demonstrates advanced Modbus TCP operations:
//   - FC 22: Mask Write Register (atomic bit manipulation)
//   - FC 23: Read/Write Multiple Registers (atomic read+write)
//   - FC 08: Diagnostics (loopback, counters)
//   - FC 43/14: Read Device Identification
//   - Multi-register typed access (Float32, Int32, String with byte order)
//   - SendRawAsync (raw function code escape hatch)
// =============================================================================

using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.Modbus;

// --- Connect with configurable byte order ---
await using var device = PlcDriverFactory.CreateModbus("192.168.1.50",
    byteOrder: ModbusByteOrder.ABCD); // Big-endian (most common)
await device.ConnectAsync();
Console.WriteLine("Connected to Modbus TCP device");

// =============================================================================
// FC 22 — Mask Write Register (Atomic Bit Manipulation)
// =============================================================================

// Set bit 3 in HR100 (without disturbing other bits)
var setBitResult = await device.SetBitAsync("HR100", 3);
Console.WriteLine($"Set bit 3 in HR100: {(setBitResult.IsSuccess ? "OK" : setBitResult.Error)}");

// Clear bit 5 in HR100
var clearBitResult = await device.ClearBitAsync("HR100", 5);
Console.WriteLine($"Clear bit 5 in HR100: {(clearBitResult.IsSuccess ? "OK" : clearBitResult.Error)}");

// Direct mask write: AND mask = 0xFF00, OR mask = 0x00FF
// This clears the low byte and sets it to 0xFF
var maskResult = await device.MaskWriteRegisterAsync("HR100", 0xFF00, 0x00FF);
Console.WriteLine($"Mask write HR100: {(maskResult.IsSuccess ? "OK" : maskResult.Error)}");

// =============================================================================
// FC 23 — Read/Write Multiple Registers (Atomic)
// =============================================================================

// Atomically write [100, 200, 300] to HR200-HR202 and read HR0-HR4
var rwResult = await device.ReadWriteMultipleRegistersAsync(
    readAddress: "HR0", readCount: 5,
    writeAddress: "HR200", writeValues: new short[] { 100, 200, 300 });

if (rwResult.IsSuccess)
{
    Console.WriteLine("\nAtomic Read/Write Results:");
    for (int i = 0; i < rwResult.ReadValues.Length; i++)
        Console.WriteLine($"  HR{i} = {rwResult.ReadValues[i]}");
}
else
{
    Console.WriteLine($"Read/Write failed: {rwResult.ErrorMessage}");
}

// =============================================================================
// FC 08 — Diagnostics
// =============================================================================

// Loopback test (echo)
var loopback = await device.DiagnosticsAsync(
    ModbusDiagnosticSubFunction.ReturnQueryData, 0x1234);
Console.WriteLine($"\nLoopback test: {(loopback.IsSuccess ? $"Echo=0x{loopback.Data:X4}" : loopback.ErrorMessage)}");

// Read bus message count
var busCount = await device.DiagnosticsAsync(
    ModbusDiagnosticSubFunction.ReturnBusMessageCount);
Console.WriteLine($"Bus message count: {(busCount.IsSuccess ? busCount.Data.ToString() : busCount.ErrorMessage)}");

// Clear all diagnostic counters
var clearResult = await device.DiagnosticsAsync(
    ModbusDiagnosticSubFunction.ClearCounters);
Console.WriteLine($"Clear counters: {(clearResult.IsSuccess ? "OK" : clearResult.ErrorMessage)}");

// =============================================================================
// FC 43/14 — Read Device Identification
// =============================================================================

try
{
    var deviceId = await device.ReadDeviceIdentificationAsync(ModbusDeviceIdLevel.Regular);
    Console.WriteLine("\nDevice Identification:");
    Console.WriteLine($"  Vendor:   {deviceId.VendorName}");
    Console.WriteLine($"  Product:  {deviceId.ProductCode}");
    Console.WriteLine($"  Revision: {deviceId.MajorMinorRevision}");
    Console.WriteLine($"  URL:      {deviceId.VendorUrl}");
    Console.WriteLine($"  Name:     {deviceId.ProductName}");
    Console.WriteLine($"  Model:    {deviceId.ModelName}");
}
catch (Exception ex)
{
    Console.WriteLine($"Device ID not supported: {ex.Message}");
}

// =============================================================================
// Multi-Register Typed Access (32-bit Float, 32-bit Int, String)
// =============================================================================

// Read a 32-bit IEEE 754 float from HR100-HR101
float temperature = await device.ReadFloat32Async("HR100");
Console.WriteLine($"\nTemperature (HR100-101): {temperature:F2}");

// Read with explicit byte order override
float pressure = await device.ReadFloat32Async("HR102", ModbusByteOrder.CDAB);
Console.WriteLine($"Pressure (HR102-103, CDAB): {pressure:F2}");

// Read a 32-bit integer from HR200-HR201
int counter = await device.ReadInt32Async("HR200");
Console.WriteLine($"Counter (HR200-201): {counter}");

// Read a string from 10 registers (20 ASCII characters)
string deviceName = await device.ReadStringAsync("HR300", registerCount: 10);
Console.WriteLine($"Device Name (HR300-309): '{deviceName}'");

// Write a 32-bit float
var writeFloat = await device.WriteFloat32Async("HR100", 98.6f);
Console.WriteLine($"\nWrite float HR100: {(writeFloat.IsSuccess ? "OK" : writeFloat.Error)}");

// Write a 32-bit integer
var writeInt = await device.WriteInt32Async("HR200", 123456);
Console.WriteLine($"Write int32 HR200: {(writeInt.IsSuccess ? "OK" : writeInt.Error)}");

// Write a string
var writeStr = await device.WriteStringAsync("HR300", "Hello Modbus!", registerCount: 10);
Console.WriteLine($"Write string HR300: {(writeStr.IsSuccess ? "OK" : writeStr.Error)}");

// =============================================================================
// SendRawAsync — Raw Function Code Escape Hatch
// =============================================================================

// Send a raw diagnostic loopback request (FC 08, sub-function 0x0000, data 0xABCD)
var rawResponse = await device.SendRawAsync(0x08,
    new byte[] { 0x00, 0x00, 0xAB, 0xCD });

Console.WriteLine($"\nRaw FC 0x08 response:");
Console.WriteLine($"  FC: 0x{rawResponse.FunctionCode:X2}");
Console.WriteLine($"  Data length: {rawResponse.Data.Length}");
Console.WriteLine($"  Exception: {rawResponse.IsException}");

Console.WriteLine("\nDone!");
