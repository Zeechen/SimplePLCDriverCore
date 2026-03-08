# SimplePLCDriverCore

A native .NET Core library for reading and writing PLC tags. No C/C++ wrappers, no P/Invoke - pure async .NET from the ground up.

## Key Features

- **Typeless tag access** - Tag types are auto-detected from PLC metadata. No need to specify `Tag<int>` or `Tag<float>`. Just read and write by tag name.
- **High-performance batch operations** - Multiple tags packed into CIP Multiple Service Packets for minimal network round-trips. Read 100 tags in 2-3 packets instead of 100.
- **Full metadata discovery** - Browse all tags, programs, and UDT definitions programmatically. The driver uploads the complete tag database on connect.
- **UDT structure support** - Read/write User Defined Types as JSON strings, strongly-typed objects, or dictionaries. Partial writes update only specified fields.
- **Native async/await** - Built on `ValueTask`, `System.IO.Pipelines`, and `ArrayPool<byte>` for zero-allocation hot paths.
- **Auto-reconnect** - Configurable connection recovery with keepalive and retry policies (fixed delay, exponential backoff with jitter).
- **Connection pooling** - Manage multiple named PLC connections with lazy connect and lifecycle management.

## Supported PLCs

| PLC Family | Status | Protocol |
|---|---|---|
| Allen-Bradley ControlLogix | Supported | EtherNet/IP + CIP |
| Allen-Bradley CompactLogix | Supported | EtherNet/IP + CIP |
| Allen-Bradley SLC 500 | Supported | PCCC over CIP |
| Allen-Bradley MicroLogix | Supported | PCCC over CIP |
| Allen-Bradley PLC-5 | Supported | PCCC over CIP |
| Siemens S7-300/400/1200/1500 | Supported | S7comm over ISO-on-TCP |
| Omron NJ/NX/CJ/CP | Supported | FINS over TCP |
| Modbus TCP devices | Planned (Phase 4) | Modbus TCP |

## Supported Data Types

### Logix (ControlLogix / CompactLogix)

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

### SLC 500 / MicroLogix / PLC-5 (PCCC)

| File Type | Prefix | .NET Type | Element Size |
|---|---|---|---|
| Integer | N | `short` | 2 bytes |
| Float | F | `float` | 4 bytes |
| Long Integer | L | `int` | 4 bytes |
| Bit | B | `bool` (per bit) | 2 bytes (word) |
| Timer | T | Structure (PRE/ACC/EN/TT/DN) | 6 bytes |
| Counter | C | Structure (PRE/ACC/CU/CD/DN) | 6 bytes |
| Control | R | Structure (LEN/POS/EN/EU/DN) | 6 bytes |
| String | ST | `string` | 84 bytes |
| Output | O | `short` | 2 bytes |
| Input | I | `short` | 2 bytes |
| Status | S | `short` | 2 bytes |
| ASCII | A | `short` | 2 bytes |

### Siemens S7

| S7 Type | Address | .NET Type | Bytes |
|---|---|---|---|
| BOOL | DB1.DBX0.0, I0.0, M0.0 | `bool` | 1 bit |
| BYTE | DB1.DBB0, IB0, MB0 | `byte` | 1 |
| WORD/INT | DB1.DBW0, IW0, MW0 | `short` | 2 |
| DWORD/DINT | DB1.DBD0, ID0, MD0 | `int` | 4 |
| REAL | DB1.DBD0 (transport=Real) | `float` | 4 |
| STRING | DB1.DBS0.20 | `string` | 2 + maxLen |
| TIMER | T0 | `short` | 2 |
| COUNTER | C0 | `short` | 2 |

### Omron FINS

| FINS Type | Address | .NET Type | Bytes |
|---|---|---|---|
| BIT | CIO0.00, D0.5 | `bool` | 1 |
| WORD | D0, CIO0, W0, H0, A0 | `short` | 2 |
| Timer PV | T0 | `short` | 2 |
| Counter PV | C0 | `short` | 2 |

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

### Retry Policies

```csharp
using SimplePLCDriverCore.Common;

// Fixed delay: 2 seconds between each of 3 attempts
var options = new ConnectionOptions
{
    AutoReconnect = true,
    ReconnectPolicy = RetryPolicy.FixedDelay(maxAttempts: 3, delay: TimeSpan.FromSeconds(2)),
};

// Exponential backoff with jitter: 1s -> 2s -> 4s -> 8s (+ random 0-50%)
// Prevents thundering herd when multiple connections retry simultaneously
var options2 = new ConnectionOptions
{
    AutoReconnect = true,
    ReconnectPolicy = RetryPolicy.ExponentialBackoff(
        maxAttempts: 5,
        baseDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromSeconds(30)),
};

// RetryPolicy can also be used standalone for your own operations
var policy = RetryPolicy.ExponentialBackoff(3, TimeSpan.FromSeconds(1));
await policy.ExecuteAsync(async (attempt, ct) =>
{
    Console.WriteLine($"Attempt {attempt}...");
    // your operation here
}, cancellationToken);
```

### Connection Pool (Multi-PLC)

```csharp
using SimplePLCDriverCore.Common;

await using var pool = new ConnectionPool();

// Register PLCs by name
pool.Register("Line1", "192.168.1.100");
pool.Register("Line2", "192.168.1.101", slot: 2);
pool.Register("Packaging", "192.168.1.102", options: new ConnectionOptions
{
    ReconnectPolicy = RetryPolicy.ExponentialBackoff(5, TimeSpan.FromSeconds(1)),
});

// Drivers connect lazily on first access
var line1 = await pool.GetAsync("Line1");
var result = await line1.ReadAsync("ProductCount");

// Check status
Console.WriteLine(pool.IsConnected("Line1"));    // true
Console.WriteLine(pool.IsConnected("Line2"));    // false (not yet accessed)

// Disconnect one (registration preserved - GetAsync will reconnect)
await pool.DisconnectAsync("Line1");

// Remove entirely
await pool.UnregisterAsync("Packaging");

// pool.DisposeAsync() disconnects everything
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

### SLC 500 / MicroLogix / PLC-5

```csharp
using SimplePLCDriverCore.Drivers;

// SLC 500
await using var slc = PlcDriverFactory.CreateSlc("192.168.1.50");
await slc.ConnectAsync();

// MicroLogix
await using var mlx = PlcDriverFactory.CreateMicroLogix("192.168.1.60");

// PLC-5
await using var plc5 = PlcDriverFactory.CreatePlc5("192.168.1.70");
```

### Siemens S7 (S7-300/400/1200/1500)

```csharp
using SimplePLCDriverCore.Drivers;

// S7-1200/1500 (rack 0, slot 0)
await using var s7 = PlcDriverFactory.CreateS7_1200("192.168.1.200");
await s7.ConnectAsync();

// S7-300/400 (rack 0, slot 2)
// await using var s7 = PlcDriverFactory.CreateS7_300("192.168.1.100");

// Custom rack/slot
// await using var s7 = PlcDriverFactory.CreateSiemens("192.168.1.200", rack: 0, slot: 2);

// Read data block values
var word = await s7.ReadAsync("DB1.DBW0");     // Word (INT, 16-bit)
var dword = await s7.ReadAsync("DB1.DBD4");    // Double word (DINT, 32-bit)
var bit = await s7.ReadAsync("DB1.DBX8.0");    // Single bit
var str = await s7.ReadAsync("DB1.DBS10.20");  // String (max 20 chars)

// Read I/O and merker areas
var input = await s7.ReadAsync("I0.0");        // Input bit
var output = await s7.ReadAsync("QW0");        // Output word
var merker = await s7.ReadAsync("MW0");        // Merker word

// Write values
await s7.WriteAsync("DB1.DBW0", (short)42);
await s7.WriteAsync("DB1.DBD4", 100000);
await s7.WriteAsync("DB1.DBX8.0", true);

// Batch read (S7 multi-item read - up to 20 items per request)
var results = await s7.ReadAsync(new[] { "DB1.DBW0", "DB1.DBD4", "MB0" });
```

### Omron FINS (NJ/NX/CJ/CP)

```csharp
using SimplePLCDriverCore.Drivers;

await using var omron = PlcDriverFactory.CreateOmron("192.168.1.100");
await omron.ConnectAsync();

// Read memory areas
var dm = await omron.ReadAsync("D100");       // DM area word
var cio = await omron.ReadAsync("CIO0");      // CIO area word
var bit = await omron.ReadAsync("CIO0.00");   // CIO bit
var work = await omron.ReadAsync("W0");       // Work area
var hold = await omron.ReadAsync("H0");       // Holding area

// Write values
await omron.WriteAsync("D100", (short)42);
await omron.WriteAsync("CIO0.00", true);

// Batch operations (sequential)
var results = await omron.ReadAsync(new[] { "D100", "D101", "W0" });
```

### SLC File-Based Addressing

```csharp
// Read integer, float, bit, string
var intResult = await slc.ReadAsync("N7:0");       // Integer file 7, element 0
var floatResult = await slc.ReadAsync("F8:1");     // Float file 8, element 1
var bitResult = await slc.ReadAsync("B3:0/5");     // Bit file 3, element 0, bit 5
var strResult = await slc.ReadAsync("ST9:0");      // String file 9, element 0
var longResult = await slc.ReadAsync("L10:0");     // Long integer file 10, element 0

// Read timer/counter structures
var timer = await slc.ReadAsync("T4:0");           // Full timer structure
var members = timer.Value.AsStructure();
Console.WriteLine($"PRE={members!["PRE"]}, ACC={members["ACC"]}, DN={members["DN"]}");

// Read timer sub-elements
var acc = await slc.ReadAsync("T4:0.ACC");         // Accumulator only
var pre = await slc.ReadAsync("T4:0.PRE");         // Preset only
var en = await slc.ReadAsync("T4:0.EN");           // Enable bit

// Write values
await slc.WriteAsync("N7:0", 42);
await slc.WriteAsync("F8:0", 3.14f);
await slc.WriteAsync("B3:0/5", true);              // Read-modify-write for bits
await slc.WriteAsync("ST9:0", "Hello SLC!");
await slc.WriteAsync("T4:0.PRE", (short)5000);     // Write timer preset

// Batch operations (sequential - PCCC does not support batching)
var results = await slc.ReadAsync(new[] { "N7:0", "N7:1", "F8:0" });
```

## Architecture

```
+------------------+------------------+------------------+------------------+
|  LogixDriver     |  SlcDriver       |  SiemensDriver   |  OmronDriver     |
|  (IPlcDriver +   |  (IPlcDriver)    |  (IPlcDriver)    |  (IPlcDriver)    |
|   ITagBrowser)   |                  |                  |                  |
+------------------+------------------+------------------+------------------+
|  CIP + Tags      |  PCCC over CIP   |  S7comm          |  FINS/TCP        |
|  TagDatabase     |  PcccCommand     |  S7Message       |  FinsMessage     |
|  MultiService    |  PcccTypes       |  S7Types         |  FinsTypes       |
+------------------+------------------+------------------+------------------+
|  EtherNet/IP Encapsulation          |  TPKT + COTP     |  FINS/TCP Frame  |
|  RegisterSession, SendRRData        |  ISO-on-TCP      |  Node Handshake  |
+-------------------------------------+------------------+------------------+
|                    TCP Transport (async)                                   |
|              System.IO.Pipelines, ArrayPool<byte>                         |
+---------------------------------------------------------------------------+
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
      Common/                      # Transport, buffers, connection management, retry, pooling
      Protocols/EtherNetIP/        # EtherNet/IP + CIP protocol implementation
        Cip/                       # CIP services, types, paths, tag operations
        Pccc/                      # PCCC protocol (SLC/MicroLogix/PLC-5)
      Protocols/S7/                # S7comm protocol (Siemens)
      Protocols/Fins/              # FINS protocol (Omron)
      Drivers/                     # High-level drivers (LogixDriver, SlcDriver, SiemensDriver, OmronDriver)
      TypeSystem/                  # Tag database, structure decode/encode
    Examples/                      # Usage examples
      BasicReadWrite/              # Logix tag read/write
      BatchOperations/             # High-performance batch operations
      TagBrowsing/                 # Metadata discovery
      ConnectionManagement/        # Connection options and patterns
      SlcReadWrite/                # SLC/MicroLogix/PLC-5 read/write
      S7ReadWrite/                 # Siemens S7 read/write
      FinsReadWrite/               # Omron FINS read/write
  tests/
    SimplePLCDriverCore.Tests/     # Unit tests (682 tests)
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
