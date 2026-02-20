// =============================================================================
// BasicReadWrite Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates basic tag read and write operations with automatic type detection.
// No type specification needed - the driver discovers tag types from the PLC.
// =============================================================================

using SimplePLCDriverCore.Drivers;

// --- Connect to a CompactLogix PLC ---
// CompactLogix is always slot 0. For ControlLogix, specify the processor slot.
await using var plc = PlcDriverFactory.CreateLogix("192.168.120.120");
await plc.ConnectAsync();
Console.WriteLine("Connected to PLC");

// =============================================================================
// Reading Tags - Types are auto-detected
// =============================================================================

// Read an integer tag (DINT)
var result = await plc.ReadAsync("TEST");
if (result.IsSuccess)
{
    Console.WriteLine($"Tag: {result.TagName}");
    Console.WriteLine($"Value: {result.Value}");
    Console.WriteLine($"Type: {result.TypeName}");    // e.g., "DINT"

    // Implicit conversion - just use the value directly
    int intValue = result.Value;
    Console.WriteLine($"As int: {intValue}");
}
else
{
    Console.WriteLine($"Read failed: {result.Error}");
}

// Read a floating-point tag (REAL)
var realResult = await plc.ReadAsync("Program:TubeBundle.Constant_Gas_Readings.InternalFlow");
if (realResult.IsSuccess)
{
    float floatValue = realResult.Value;  // implicit conversion
    Console.WriteLine($"REAL value: {floatValue}");
}

// Read a boolean tag
var boolResult = await plc.ReadAsync("Program:TubeBundle.ControlMode_Online");
if (boolResult.IsSuccess)
{
    bool flag = boolResult.Value;
    Console.WriteLine($"BOOL value: {flag}");
}

// Read a string tag
var strResult = await plc.ReadAsync("STR");
if (strResult.IsSuccess)
{
    string text = strResult.Value;
    Console.WriteLine($"STRING value: {text}");
}

// Read a 64-bit float (LREAL)
var lrealResult = await plc.ReadAsync("MyLREAL");
if (lrealResult.IsSuccess)
{
    double dblValue = lrealResult.Value;
    Console.WriteLine($"LREAL value: {dblValue}");
}

// =============================================================================
// Writing Tags - Types are auto-detected from PLC metadata
// =============================================================================

// Write an integer
var writeResult = await plc.WriteAsync("MyDINT", 42);
Console.WriteLine($"Write DINT: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a float
writeResult = await plc.WriteAsync("MyREAL", 3.14f);
Console.WriteLine($"Write REAL: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a boolean
writeResult = await plc.WriteAsync("MyBOOL", true);
Console.WriteLine($"Write BOOL: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a string
writeResult = await plc.WriteAsync("MySTRING", "Hello PLC!");
Console.WriteLine($"Write STRING: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// Write a double (LREAL)
writeResult = await plc.WriteAsync("MyLREAL", 2.718281828);
Console.WriteLine($"Write LREAL: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// Using GetValueOrThrow for exception-based error handling
// =============================================================================

try
{
    var value = (await plc.ReadAsync("MyDINT")).GetValueOrThrow();
    Console.WriteLine($"Value (exception style): {value}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

// =============================================================================
// Reading Array Elements
// =============================================================================

// Read a specific array element
var arrayElement = await plc.ReadAsync("MyArray[5]");
if (arrayElement.IsSuccess)
{
    Console.WriteLine($"MyArray[5] = {arrayElement.Value}");
}

// =============================================================================
// Reading UDT (Structure) Tags
// =============================================================================

var udtResult = await plc.ReadAsync("MyUDT");
if (udtResult.IsSuccess)
{
    Console.WriteLine($"UDT Type: {udtResult.TypeName}");
    Console.WriteLine($"UDT Value: {udtResult.Value}");

    // Access structure members
    var members = udtResult.Value.AsStructure();
    if (members != null)
    {
        foreach (var (name, value) in members)
        {
            Console.WriteLine($"  {name} = {value}");
        }
    }
}

// =============================================================================
// Accessing UDT Members Directly (Read)
// =============================================================================

// You can read a specific member of a UDT using dot notation
var memberResult = await plc.ReadAsync("MyUDT.IntField");
if (memberResult.IsSuccess)
{
    Console.WriteLine($"MyUDT.IntField = {memberResult.Value}");
}

// =============================================================================
// Reading a UDT as JSON
// =============================================================================

// ReadJsonAsync returns the entire UDT as a JSON string - no dictionary step.
var jsonResult = await plc.ReadJsonAsync("MyUDT");
if (jsonResult.IsSuccess)
{
    Console.WriteLine($"UDT as JSON: {jsonResult.Value}");
    // e.g.: {"IntField":42,"FloatField":3.14,"BoolField":true}
}

// =============================================================================
// Reading a UDT as a Strongly-Typed Object
// =============================================================================

// Define a class that matches the UDT structure (see MyUdtType at the bottom).
// Property names are matched case-insensitively to UDT member names.
var myUdt = await plc.ReadAsync<MyUdtType>("MyUDT");
if (myUdt.IsSuccess)
{
    Console.WriteLine($"IntField: {myUdt.Value!.IntField}");
    Console.WriteLine($"FloatField: {myUdt.Value!.FloatField}");
}

// =============================================================================
// Writing an Entire UDT from a Typed Object
// =============================================================================

// WriteAsync<T> serializes the object to JSON, then encodes directly to CIP bytes.
// All properties in the object are written to the PLC.
var obj = new MyUdtType { IntField = 42, FloatField = 3.14f, BoolField = true };
var udtWriteResult = await plc.WriteAsync("MyUDT", obj);
Console.WriteLine($"Write UDT: {(udtWriteResult.IsSuccess ? "OK" : udtWriteResult.Error)}");

// =============================================================================
// Partial UDT Write from JSON (Read-Modify-Write)
// =============================================================================

// WriteJsonAsync only updates the fields included in the JSON string.
// Fields not in the JSON keep their current PLC values.
// This performs a read-modify-write internally.
var partialResult = await plc.WriteJsonAsync("MyUDT", """{"IntField": 100}""");
Console.WriteLine($"Partial write: {(partialResult.IsSuccess ? "OK" : partialResult.Error)}");

// =============================================================================
// Writing a Single UDT Member (Dot Notation)
// =============================================================================

// Write a specific member of a UDT using dot notation.
// The driver resolves the member's actual type (e.g., DINT, REAL)
// from the UDT definition, so you don't need to know the type.
writeResult = await plc.WriteAsync("MyUDT.IntField", 100);
Console.WriteLine($"Write UDT.IntField: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

writeResult = await plc.WriteAsync("MyUDT.FloatField", 6.28f);
Console.WriteLine($"Write UDT.FloatField: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

writeResult = await plc.WriteAsync("MyUDT.BoolField", false);
Console.WriteLine($"Write UDT.BoolField: {(writeResult.IsSuccess ? "OK" : writeResult.Error)}");

// =============================================================================
// PlcTagValue.ToJson() - Convert any existing PlcTagValue to JSON
// =============================================================================

// If you already have a PlcTagValue from ReadAsync, you can get JSON from it too.
var readResult2 = await plc.ReadAsync("MyUDT");
if (readResult2.IsSuccess)
{
    string json = readResult2.Value.ToJson(indented: true);
    Console.WriteLine($"PlcTagValue as JSON:\n{json}");
}

Console.WriteLine("\nDone!");

// =============================================================================
// Example UDT class (matches PLC UDT structure)
// =============================================================================

public class MyUdtType
{
    public int IntField { get; set; }
    public float FloatField { get; set; }
    public bool BoolField { get; set; }
}
