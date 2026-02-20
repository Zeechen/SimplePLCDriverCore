# SimplePLCDriverCore

A native .NET Core library for reading and writing PLC tags. No C/C++ wrappers, no P/Invoke - pure async .NET from the ground up.

## Key Features

- **Typeless tag access** - Tag types are auto-detected from PLC metadata. No need to specify `Tag<int>` or `Tag<float>`. Just read and write by tag name.
- **High-performance batch operations** - Multiple tags packed into CIP Multiple Service Packets for minimal network round-trips. Read 100 tags in 2-3 packets instead of 100.
- **Full metadata discovery** - Browse all tags, programs, and UDT definitions programmatically. The driver uploads the complete tag database on connect.
- **UDT structure support** - Read/write User Defined Types as JSON strings, strongly-typed objects, or dictionaries. Partial writes update only specified fields.
- **Native async/await** - Built on `ValueTask`, `System.IO.Pipelines`, and `ArrayPool<byte>` for zero-allocation hot paths.
- **Auto-reconnect** - Configurable connection recovery with keepalive.

## Supported PLCs

| PLC Family | Status | Protocol |
|---|---|---|
| Allen-Bradley ControlLogix | Supported | EtherNet/IP + CIP |
| Allen-Bradley CompactLogix | Supported | EtherNet/IP + CIP |
| Allen-Bradley SLC 500 | Planned (Phase 2) | PCCC over CIP |
| Allen-Bradley MicroLogix | Planned (Phase 2) | PCCC over CIP |
| Allen-Bradley PLC-5 | Planned (Phase 2) | PCCC over CIP |
| Siemens S7-300/400/1200/1500 | Planned (Phase 3) | S7comm |
| Omron NJ/NX/CJ/CP | Planned (Phase 3) | FINS |
| Modbus TCP devices | Planned (Phase 4) | Modbus TCP |

## Supported Data Types

| PLC Type | .NET Type | Code |
|---|---|---|
| BOOL | `bool` | 0xC1 |
| SINT | `sbyte` | 0xC2 |
| INT | `short` | 0xC3 |
| DINT | `int` | 0xC4 |
| LINT | `long` | 0xC5 |
| USINT | `byte` | 0xC6 |
| UINT | `ushort` | 0xC7 |
| UDINT | `uint` | 0xC8 |
| ULINT | `ulong` | 0xC9 |
| REAL | `float` | 0xCA |
| LREAL | `double` | 0xCB |
| STRING | `string` | 0xDA |
| UDT/Structure | JSON / typed object / `Dictionary<string, PlcTagValue>` | 0x8xxx |

## Quick Start

### Install

```
dotnet add package SimplePLCDriverCore
```

### Read and Write Tags

```csharp
using SimplePLCDriverCore.Drivers;

await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100");
await plc.ConnectAsync();

// Read - type auto-detected from PLC
var result = await plc.ReadAsync("MyDINT");
Console.WriteLine($"{result.TagName} = {result.Value} ({result.TypeName})");
// Output: MyDINT = 42 (DINT)

// Implicit conversion to .NET types
int value = result.Value;
float floatVal = (await plc.ReadAsync("MyREAL")).Value;

// Write - type auto-detected
await plc.WriteAsync("MyDINT", 100);
await plc.WriteAsync("MyREAL", 3.14f);
await plc.WriteAsync("MySTRING", "Hello PLC");
```

### Batch Read/Write (High Performance)

```csharp
// Read 100 tags in minimal network round-trips
var results = await plc.ReadAsync(new[] { "Tag1", "Tag2", "Tag3", /* ... */ "Tag100" });

foreach (var r in results)
{
    if (r.IsSuccess)
        Console.WriteLine($"{r.TagName} = {r.Value} ({r.TypeName})");
    else
        Console.WriteLine($"{r.TagName} ERROR: {r.Error}");
}

// Batch write
await plc.WriteAsync(new[]
{
    ("Setpoint", (object)75.5f),
    ("Speed", (object)1200),
    ("RunStatus", (object)true),
});
```

### Batch Read/Write with UDT Dot Notation

```csharp
// Mix scalar tags and UDT member paths in a single batch read
var results = await plc.ReadAsync(new[]
{
    "Temperature",          // Scalar tag
    "Motor1.Speed",         // UDT member
    "Motor1.Current",       // Another member of the same UDT
    "Motor2.Speed",         // Member from a different UDT instance
    "Reactor.Setpoint",     // Member from a different UDT type
});

// Batch write to UDT members from different tags
await plc.WriteAsync(new[]
{
    ("Motor1.Speed", (object)1500),
    ("Motor1.Enabled", (object)true),
    ("Motor2.Speed", (object)1200),
    ("Reactor.Setpoint", (object)75.5f),
    ("AlarmHigh", (object)100.0f),       // Scalar in the same batch
});
```

### Browse Tags and UDTs

```csharp
using SimplePLCDriverCore.Abstractions;

// LogixDriver implements ITagBrowser
var browser = (ITagBrowser)plc;

// List all controller-scoped tags
var tags = await browser.GetTagsAsync();
foreach (var tag in tags)
    Console.WriteLine($"{tag.Name}: {tag.TypeName}");

// List programs
var programs = await browser.GetProgramsAsync();
foreach (var prog in programs)
{
    var progTags = await browser.GetProgramTagsAsync(prog);
    Console.WriteLine($"Program {prog}: {progTags.Count} tags");
}

// Browse UDT definitions
var udts = await browser.GetAllUdtDefinitionsAsync();
foreach (var udt in udts)
{
    Console.WriteLine($"UDT: {udt.Name} ({udt.ByteSize} bytes)");
    foreach (var member in udt.Members)
        Console.WriteLine($"  {member.Name}: {member.TypeName} @ offset {member.Offset}");
}
```

### Read UDT as JSON

```csharp
// Read UDT directly as a JSON string (no intermediate dictionary)
var jsonResult = await plc.ReadJsonAsync("MyUDT");
Console.WriteLine(jsonResult.Value);
// Output: {"IntField":42,"FloatField":3.14,"BoolField":true}
```

### Read UDT as a Typed Object

```csharp
// Define a class matching the UDT structure
public class MyUdtType
{
    public int IntField { get; set; }
    public float FloatField { get; set; }
    public bool BoolField { get; set; }
}

// Deserialize directly into your class
var result = await plc.ReadAsync<MyUdtType>("MyUDT");
Console.WriteLine(result.Value!.IntField);   // 42
Console.WriteLine(result.Value!.FloatField); // 3.14
```

### Write UDT from a Typed Object (Full Write)

```csharp
// All properties are written to the PLC
var obj = new MyUdtType { IntField = 42, FloatField = 3.14f, BoolField = true };
await plc.WriteAsync("MyUDT", obj);
```

### Write UDT from JSON (Partial Write)

```csharp
// Only the fields in the JSON are updated.
// Other fields keep their current PLC values (read-modify-write).
await plc.WriteJsonAsync("MyUDT", """{"IntField": 100}""");
// FloatField and BoolField remain unchanged on the PLC.
```

### Write a Single UDT Member (Dot Notation)

```csharp
// Write a specific member using dot notation
await plc.WriteAsync("MyUDT.IntField", 100);
await plc.WriteAsync("MyUDT.FloatField", 6.28f);
```

### Read UDT Members Directly

```csharp
// Read a specific member of a UDT
var field = await plc.ReadAsync("MyUDT.IntField");
int fieldValue = field.Value;

// Read the whole UDT with PlcTagValue (dictionary style)
var udtResult = await plc.ReadAsync("MyUDT");
var members = udtResult.Value.AsStructure();
Console.WriteLine(members["IntField"]);   // 42

// Convert any PlcTagValue to JSON
string json = udtResult.Value.ToJson(indented: true);
```

### Connection Options

```csharp
using SimplePLCDriverCore.Common;

var options = new ConnectionOptions
{
    ConnectTimeout = TimeSpan.FromSeconds(10),
    RequestTimeout = TimeSpan.FromSeconds(15),
    KeepAliveInterval = TimeSpan.FromSeconds(20),
    AutoReconnect = true,
    MaxReconnectAttempts = 5,
    ReconnectDelay = TimeSpan.FromSeconds(3),
    Slot = 0,
};

await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100", options: options);
await plc.ConnectAsync();
```

### ControlLogix vs CompactLogix

```csharp
// CompactLogix - always slot 0
await using var compact = PlcDriverFactory.CreateCompactLogix("192.168.1.100");

// ControlLogix - specify the processor slot
await using var controlLogix = PlcDriverFactory.CreateControlLogix("192.168.1.100", slot: 2);

// Or use the generic factory
await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100", slot: 0);
```

## Architecture

```
+-------------------------------------------+
|     LogixDriver (IPlcDriver + ITagBrowser) |   Drivers/
|  ReadAsync, WriteAsync, GetTagsAsync, ...  |
+-------------------------------------------+
|        TagOperations + TagDatabase         |   TypeSystem/
|  Batch, fragmented, structure decode/encode|
+-------------------------------------------+
|         CIP Application Layer              |   Protocols/EtherNetIP/Cip/
|  ReadTag, WriteTag, MultiServicePacket,    |
|  ForwardOpen, SymbolObject, TemplateObject |
+-------------------------------------------+
|       EtherNet/IP Encapsulation            |   Protocols/EtherNetIP/
|  RegisterSession, SendRRData, SendUnitData |
+-------------------------------------------+
|       TCP Transport (async)                |   Common/Transport/
|  System.IO.Pipelines, ArrayPool<byte>      |
+-------------------------------------------+
```

## How Typeless Access Works

1. On `ConnectAsync()`, the driver uploads the full tag database from the PLC using CIP Symbol Object (Class 0x6B).
2. For each structure tag, UDT definitions are read via CIP Template Object (Class 0x6C).
3. All tag names, types, and dimensions are cached in memory.
4. When you call `ReadAsync("MyTag")`, the driver already knows the type and automatically encodes/decodes the value.
5. Results are returned as `PlcTagValue` - a universal wrapper with implicit conversion to `int`, `float`, `double`, `bool`, `string`, etc.

This is the same approach used by [pycomm3](https://github.com/ottowayi/pycomm3), adapted for .NET.

## Project Structure

```
SimplePLCDriverCore/
  src/
    SimplePLCDriverCore/           # Core library
      Abstractions/                # Public interfaces (IPlcDriver, ITagBrowser, PlcTagValue, TagResult)
      Common/                      # Transport, buffers, connection management
      Protocols/EtherNetIP/        # EtherNet/IP + CIP protocol implementation
      Drivers/                     # High-level drivers (LogixDriver)
      TypeSystem/                  # Tag database, structure decode/encode
    Examples/                      # Usage examples
      BasicReadWrite/              # Single tag read/write
      BatchOperations/             # High-performance batch operations
      TagBrowsing/                 # Metadata discovery
      ConnectionManagement/        # Connection options and patterns
  tests/
    SimplePLCDriverCore.Tests/     # Unit tests (275 tests)
```

## Requirements

- .NET 8.0 or later
- Zero external NuGet dependencies (only `System.IO.Pipelines` which ships with .NET 8)

## Design Principles

- **No type specification required** - The driver discovers types automatically from PLC metadata.
- **Result pattern over exceptions** - Batch operations return `TagResult` per tag. Individual failures don't throw.
- **Zero-allocation hot path** - Uses `ArrayPool<byte>`, `Span<T>`, `ValueTask<T>` throughout.
- **Pure .NET** - No native dependencies, no P/Invoke. Works with NativeAOT and trimming.
- **Async-first** - Every I/O operation is natively async. No sync-over-async wrappers.

## Comparison with libplctag.NET

| Feature | libplctag.NET | SimplePLCDriverCore |
|---|---|---|
| Implementation | C/C++ with P/Invoke | Pure .NET |
| Tag access | `Tag<int>`, `Tag<float>` (must know type) | Typeless - auto-detected |
| Batch operations | Sequential under the hood | CIP Multiple Service Packet |
| Tag browsing | Manual | Automatic on connect |
| UDT support | Raw bytes | JSON, typed objects, or dictionaries |
| Async I/O | Wrapper over sync C calls | Native async with Pipelines |
| NativeAOT | Requires native libs per platform | Works out of the box |
| Dependencies | libplctag C library | Zero (only BCL) |

## License

MIT
