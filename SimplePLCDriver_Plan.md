# SimplePLCDriverCore - Implementation Plan & Proposal

## 1. Executive Summary

**SimplePLCDriverCore** is a native .NET Core library for reading and writing PLC tags across multiple PLC platforms. It aims to combine the best design ideas from **pycomm3** (typeless tag access, automatic metadata discovery) and **libplctag** (multi-vendor support) while delivering a pure .NET implementation with superior async performance.

### Key Differentiators vs. Existing Libraries

| Feature | libplctag.NET | SimplePLCDriverCore |
|---------|--------------|---------------------|
| Implementation | C/C++ with .NET P/Invoke wrapper | Pure native .NET Core |
| Tag type handling | Strongly typed (`Tag<int>`, `Tag<float>`) | Typeless - auto-detected from PLC metadata |
| Metadata discovery | Limited (manual tag browsing) | Full: tag lists, programs, UDTs, AOIs |
| Async support | Limited (wrapper over sync C calls) | Native `async/await` with `ValueTask` |
| Batch operations | Sequential under the hood | True CIP Multiple Service Packet batching |
| Multi-PLC protocols | Via C library | Native .NET protocol implementations |

### Target PLC Support

| Priority | PLC Family | Protocol | Phase |
|----------|-----------|----------|-------|
| P0 (Must) | Allen-Bradley ControlLogix | EtherNet/IP + CIP | Phase 1 |
| P0 (Must) | Allen-Bradley CompactLogix | EtherNet/IP + CIP | Phase 1 |
| P1 (Should) | Allen-Bradley SLC 500 | PCCC over CIP | Phase 2 |
| P1 (Should) | Allen-Bradley MicroLogix | PCCC over CIP | Phase 2 |
| P1 (Should) | Allen-Bradley PLC-5 | PCCC over CIP | Phase 2 |
| P2 (Nice) | Siemens S7-300/400/1200/1500 | S7comm (ISO-on-TCP) | Phase 3 |
| P2 (Nice) | Omron NJ/NX/CJ/CP | FINS (TCP/UDP) | Phase 3 |
| P3 (Future) | Any Modbus device | Modbus TCP | Phase 4 |

---

## 2. Research Findings Summary

### 2.1 pycomm3 Architecture (Key Inspiration)

pycomm3 is a pure Python library that provides the best developer experience for AB PLCs:

- **Typeless tag access**: On connection, `LogixDriver` automatically uploads the entire tag database from the PLC using CIP Symbol Object (Class 0x6B) and Template Object (Class 0x6C). This means the driver already knows every tag's data type before any read/write, so users never specify types.
- **Result pattern**: All operations return a `Tag` dataclass with `(tag_name, value, type_name, error)` - no exceptions for individual tag failures in batch ops.
- **Batch operations**: Uses CIP Multiple Service Packet (service 0x0A) to pack multiple reads/writes into a single network round-trip.
- **UDT support**: Automatically reads template definitions and decodes structures into Python dicts.
- **Clean hierarchy**: `CIPDriver` (transport) -> `LogixDriver` (ControlLogix/CompactLogix) / `SLCDriver` (SLC/MicroLogix/PLC5).

### 2.2 libplctag Limitations

- **P/Invoke overhead**: Every tag operation crosses the managed/native boundary.
- **Raw byte buffers**: All data returned as byte arrays - caller must know type and offset.
- **No metadata APIs**: No built-in tag browsing, UDT template reading, or program enumeration.
- **Thread-based concurrency**: Uses OS threads internally, not async I/O.

### 2.3 .NET Library Landscape Gap

The research confirmed that **no unified, pure-.NET, async-first, permissively-licensed library exists** that provides:
- Common abstraction across multiple PLC protocols
- Automatic tag type detection
- Full metadata discovery
- True async I/O with batching

Existing .NET libraries:
- **S7netplus**: Siemens only, sync-only, address-based (not typeless)
- **NModbus / FluentModbus**: Modbus only, register-based
- **libplctag.NET**: Multi-vendor but C/C++ wrapper with limitations listed above

### 2.4 Protocol Technical Details

**EtherNet/IP + CIP** (Allen-Bradley):
- TCP port 44818, little-endian
- 24-byte encapsulation header
- Session registration -> Forward Open -> Read/Write Tag services
- Tag names encoded as ANSI symbolic segments (0x91)
- Multiple Service Packet (0x0A) for batching
- Tag discovery via Symbol Object (0x6B), UDT templates via Template Object (0x6C)

**PCCC over CIP** (SLC/MicroLogix/PLC5):
- Same EtherNet/IP transport
- CIP wraps PCCC commands (Protected Typed Logical Read/Write)
- File-based addressing (N7:0, F8:1, B3:0/5)

**S7comm** (Siemens):
- TCP port 102, big-endian
- TPKT -> COTP -> S7comm layered protocol
- 3-phase connection: TCP, COTP handshake (TSAP), S7 negotiation
- Absolute addressing: area + DB number + byte offset * 8 + bit

**FINS** (Omron):
- TCP/UDP port 9600, big-endian
- Memory area read/write commands (0x0101 / 0x0102)

**Modbus TCP**:
- TCP port 502, big-endian (protocol fields)
- MBAP header + function codes (0x01-0x10)
- 16-bit register model

---

## 3. Architecture Design

### 3.1 Solution Structure

```
SimplePLCDriverCore/
|
+-- src/
|   +-- SimplePLCDriverCore/                     # Main library project
|   |   +-- SimplePLCDriverCore.csproj
|   |   |
|   |   +-- Abstractions/                        # Public interfaces & contracts
|   |   |   +-- IPlcDriver.cs                    # Core driver interface
|   |   |   +-- ITagBrowser.cs                   # Tag/metadata discovery
|   |   |   +-- IPlcConnection.cs                # Connection management
|   |   |   +-- PlcValue.cs                      # Universal typeless value wrapper
|   |   |   +-- TagResult.cs                     # Operation result type
|   |   |   +-- PlcTagInfo.cs                    # Tag metadata descriptor
|   |   |   +-- PlcDataType.cs                   # Data type enumeration
|   |   |   +-- UdtDefinition.cs                 # UDT structure definition
|   |   |
|   |   +-- Common/                              # Shared infrastructure
|   |   |   +-- Transport/
|   |   |   |   +-- TcpTransport.cs              # Async TCP socket wrapper
|   |   |   |   +-- ITransport.cs                # Transport abstraction
|   |   |   +-- Buffers/
|   |   |   |   +-- PacketBuffer.cs              # High-perf buffer (System.Buffers)
|   |   |   |   +-- PacketReader.cs              # Little/big endian reader
|   |   |   |   +-- PacketWriter.cs              # Little/big endian writer
|   |   |   +-- ConnectionPool.cs                # Connection pooling
|   |   |   +-- RetryPolicy.cs                   # Reconnection logic
|   |   |
|   |   +-- Protocols/
|   |   |   +-- EtherNetIP/                      # EtherNet/IP + CIP stack
|   |   |   |   +-- EipSession.cs                # EtherNet/IP session management
|   |   |   |   +-- EipEncapsulation.cs           # 24-byte header encode/decode
|   |   |   |   +-- Cip/
|   |   |   |   |   +-- CipMessage.cs            # CIP message builder
|   |   |   |   |   +-- CipPath.cs               # Symbolic/logical path encoding
|   |   |   |   |   +-- CipTypes.cs              # CIP data type codes & codec
|   |   |   |   |   +-- CipServices.cs           # Service code constants
|   |   |   |   |   +-- ForwardOpen.cs           # Connection establishment
|   |   |   |   |   +-- MultiServicePacket.cs    # Batch request builder
|   |   |   |   |   +-- SymbolObject.cs          # Class 0x6B tag browsing
|   |   |   |   |   +-- TemplateObject.cs        # Class 0x6C UDT reading
|   |   |   |   +-- Pccc/
|   |   |   |   |   +-- PcccCommand.cs           # PCCC command builder
|   |   |   |   |   +-- PcccAddress.cs           # SLC address parser
|   |   |   |   |   +-- PcccTypes.cs             # PCCC data types
|   |   |   |
|   |   |   +-- S7/                              # Siemens S7comm stack
|   |   |   |   +-- S7Session.cs                 # TPKT+COTP+S7 session
|   |   |   |   +-- S7Message.cs                 # S7 read/write PDU builder
|   |   |   |   +-- S7Address.cs                 # DB/area addressing
|   |   |   |   +-- S7Types.cs                   # S7 data types
|   |   |   |   +-- CotpPacket.cs                # ISO transport (COTP)
|   |   |   |   +-- TpktPacket.cs                # TPKT framing
|   |   |   |
|   |   |   +-- Fins/                            # Omron FINS stack
|   |   |   |   +-- FinsSession.cs
|   |   |   |   +-- FinsMessage.cs
|   |   |   |   +-- FinsAddress.cs
|   |   |   |
|   |   |   +-- Modbus/                          # Modbus TCP stack
|   |   |       +-- ModbusSession.cs
|   |   |       +-- ModbusMessage.cs
|   |   |
|   |   +-- Drivers/                             # High-level driver implementations
|   |   |   +-- LogixDriver.cs                   # ControlLogix/CompactLogix
|   |   |   +-- SlcDriver.cs                     # SLC/MicroLogix/PLC-5
|   |   |   +-- SiemensDriver.cs                 # Siemens S7
|   |   |   +-- OmronDriver.cs                   # Omron FINS
|   |   |   +-- ModbusDriver.cs                  # Modbus TCP
|   |   |   +-- PlcDriverFactory.cs              # Factory for creating drivers
|   |   |
|   |   +-- TypeSystem/                          # Typeless value system
|   |       +-- PlcValueConverter.cs             # Type detection & conversion
|   |       +-- StructureDecoder.cs              # UDT byte[] -> PlcValue decoder
|   |       +-- StructureEncoder.cs              # PlcValue -> byte[] encoder
|   |       +-- DataTypeRegistry.cs              # Per-connection type cache
|   |
|   +-- SimplePLCDriverCore.Extensions/          # Optional DI/hosting extensions
|       +-- ServiceCollectionExtensions.cs
|       +-- PlcHealthCheck.cs
|
+-- tests/
|   +-- SimplePLCDriverCore.Tests/               # Unit tests
|   +-- SimplePLCDriverCore.IntegrationTests/    # Integration tests (real PLC)
|
+-- samples/
|   +-- BasicReadWrite/                          # Simple console app
|   +-- BatchOperations/                         # Multi-tag batch demo
|   +-- TagBrowsing/                             # Metadata discovery demo
|   +-- MultiPlcTypes/                           # Cross-vendor demo
|
+-- SimplePLCDriverCore.sln
+-- README.md
+-- LICENSE
```

### 3.2 Core Abstractions

#### 3.2.1 `IPlcDriver` - The Primary Interface

```csharp
public interface IPlcDriver : IAsyncDisposable, IDisposable
{
    /// <summary>Connection state</summary>
    bool IsConnected { get; }

    /// <summary>PLC identity information (name, firmware, serial, etc.)</summary>
    PlcInfo Info { get; }

    /// <summary>Open connection to PLC</summary>
    ValueTask ConnectAsync(CancellationToken ct = default);

    /// <summary>Close connection</summary>
    ValueTask DisconnectAsync(CancellationToken ct = default);

    // --- Single Tag Operations ---

    /// <summary>Read a single tag - type auto-detected</summary>
    ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default);

    /// <summary>Write a single tag - type auto-detected from metadata</summary>
    ValueTask<TagResult> WriteAsync(string tagName, object value, CancellationToken ct = default);

    // --- Batch Operations (high-performance) ---

    /// <summary>Read multiple tags in one network round-trip</summary>
    ValueTask<TagResult[]> ReadAsync(IEnumerable<string> tagNames, CancellationToken ct = default);

    /// <summary>Write multiple tags in one network round-trip</summary>
    ValueTask<TagResult[]> WriteAsync(
        IEnumerable<(string TagName, object Value)> tags,
        CancellationToken ct = default);
}
```

#### 3.2.2 `ITagBrowser` - Metadata Discovery

```csharp
public interface ITagBrowser
{
    /// <summary>Get all controller-scoped tags</summary>
    ValueTask<IReadOnlyList<PlcTagInfo>> GetTagsAsync(CancellationToken ct = default);

    /// <summary>Get tags for a specific program</summary>
    ValueTask<IReadOnlyList<PlcTagInfo>> GetProgramTagsAsync(
        string programName, CancellationToken ct = default);

    /// <summary>Get all program names</summary>
    ValueTask<IReadOnlyList<string>> GetProgramsAsync(CancellationToken ct = default);

    /// <summary>Get UDT definition by name</summary>
    ValueTask<UdtDefinition?> GetUdtDefinitionAsync(
        string typeName, CancellationToken ct = default);

    /// <summary>Get all UDT definitions</summary>
    ValueTask<IReadOnlyList<UdtDefinition>> GetAllUdtDefinitionsAsync(
        CancellationToken ct = default);
}
```

#### 3.2.3 `PlcValue` - Universal Typeless Value

This is the core innovation - inspired by pycomm3's approach but adapted for .NET:

```csharp
/// <summary>
/// Universal PLC value container that wraps any tag value
/// without requiring the consumer to know the PLC data type upfront.
/// Supports implicit conversion to common .NET types.
/// </summary>
public readonly struct PlcValue
{
    private readonly object _value;
    public PlcDataType DataType { get; }

    // Display-friendly: always works regardless of underlying type
    public override string ToString() => _value?.ToString() ?? "null";

    // Explicit accessors with safe conversion
    public bool AsBoolean() => Convert.ToBoolean(_value);
    public short AsInt16() => Convert.ToInt16(_value);
    public int AsInt32() => Convert.ToInt32(_value);
    public long AsInt64() => Convert.ToInt64(_value);
    public float AsSingle() => Convert.ToSingle(_value);
    public double AsDouble() => Convert.ToDouble(_value);
    public string AsString() => _value?.ToString() ?? string.Empty;

    // For UDTs - access as dictionary
    public IReadOnlyDictionary<string, PlcValue>? AsStructure() => ...;

    // For arrays
    public IReadOnlyList<PlcValue>? AsArray() => ...;

    // Generic accessor (with type checking)
    public T As<T>() => (T)Convert.ChangeType(_value, typeof(T));

    // Implicit conversions for convenience
    public static implicit operator int(PlcValue v) => v.AsInt32();
    public static implicit operator float(PlcValue v) => v.AsSingle();
    public static implicit operator double(PlcValue v) => v.AsDouble();
    public static implicit operator bool(PlcValue v) => v.AsBoolean();
    public static implicit operator string(PlcValue v) => v.AsString();
}
```

#### 3.2.4 `TagResult` - Operation Result

```csharp
/// <summary>
/// Result of a tag read/write operation.
/// Uses Result pattern (no exceptions for individual tag failures in batch).
/// </summary>
public readonly struct TagResult
{
    public string TagName { get; }
    public PlcValue Value { get; }
    public string TypeName { get; }        // e.g., "DINT", "REAL", "MyUDT"
    public bool IsSuccess { get; }
    public string? Error { get; }          // null on success

    // Convenience: throw if failed
    public PlcValue GetValueOrThrow() =>
        IsSuccess ? Value : throw new PlcOperationException(TagName, Error!);
}
```

#### 3.2.5 `PlcTagInfo` - Tag Metadata

```csharp
public class PlcTagInfo
{
    public string Name { get; }
    public PlcDataType DataType { get; }
    public string TypeName { get; }        // "DINT", "REAL", "MyCustomUDT"
    public int[] Dimensions { get; }       // [0] for scalar, [10] for 1D array, etc.
    public bool IsStructure { get; }
    public bool IsProgramScoped { get; }
    public string? ProgramName { get; }
    public int InstanceId { get; }         // CIP symbol instance ID
}
```

### 3.3 Usage Examples (Target API)

#### Basic Read/Write (Typeless)

```csharp
// Like pycomm3 - no types needed!
await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100");
await plc.ConnectAsync();

// Read - type auto-detected from PLC metadata
TagResult result = await plc.ReadAsync("MyDINT");
Console.WriteLine(result.Value);              // prints: 42
Console.WriteLine(result.TypeName);           // prints: DINT

// Write - type auto-detected
await plc.WriteAsync("MyReal", 3.14);

// UDT access - returns as dictionary
TagResult udt = await plc.ReadAsync("MyUDT");
var structure = udt.Value.AsStructure();
Console.WriteLine(structure["Field1"]);       // prints: 100
Console.WriteLine(structure["Field2"]);       // prints: 3.14

// Implicit conversion for convenience
int val = (await plc.ReadAsync("MyDINT")).Value;
```

#### Batch Operations (High Performance)

```csharp
// Read 100 tags in minimal network round-trips
var tagNames = new[] { "Tag1", "Tag2", "Tag3", /* ... */ "Tag100" };
TagResult[] results = await plc.ReadAsync(tagNames);

foreach (var r in results)
{
    if (r.IsSuccess)
        Console.WriteLine($"{r.TagName} = {r.Value} ({r.TypeName})");
    else
        Console.WriteLine($"{r.TagName} ERROR: {r.Error}");
}

// Write multiple tags in one batch
await plc.WriteAsync(new[]
{
    ("IntTag", (object)42),
    ("RealTag", (object)3.14f),
    ("StringTag", (object)"Hello PLC"),
});
```

#### Tag Browsing & Metadata

```csharp
await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100");
await plc.ConnectAsync();

// Browse all tags
var browser = (ITagBrowser)plc;
var allTags = await browser.GetTagsAsync();

foreach (var tag in allTags)
    Console.WriteLine($"{tag.Name}: {tag.TypeName} [{string.Join(",", tag.Dimensions)}]");

// List programs
var programs = await browser.GetProgramsAsync();
foreach (var prog in programs)
{
    Console.WriteLine($"Program: {prog}");
    var progTags = await browser.GetProgramTagsAsync(prog);
    foreach (var t in progTags)
        Console.WriteLine($"  {t.Name}: {t.TypeName}");
}

// View UDT definitions
var udts = await browser.GetAllUdtDefinitionsAsync();
foreach (var udt in udts)
{
    Console.WriteLine($"UDT: {udt.Name} ({udt.ByteSize} bytes)");
    foreach (var member in udt.Members)
        Console.WriteLine($"  {member.Name}: {member.TypeName} @ offset {member.Offset}");
}
```

#### SLC/MicroLogix (File-based addressing)

```csharp
await using var plc = PlcDriverFactory.CreateSlc("192.168.1.50");
await plc.ConnectAsync();

// SLC-style addressing
var intVal = await plc.ReadAsync("N7:0");     // Integer file 7, element 0
var floatVal = await plc.ReadAsync("F8:1");   // Float file 8, element 1
var bitVal = await plc.ReadAsync("B3:0/5");   // Bit file 3, element 0, bit 5

await plc.WriteAsync("N7:0", 100);
```

#### Siemens S7 (Phase 3)

```csharp
await using var plc = PlcDriverFactory.CreateSiemens("192.168.1.200", rack: 0, slot: 1);
await plc.ConnectAsync();

// DB addressing
var val = await plc.ReadAsync("DB1.DBD0");    // Data Block 1, Double Word at byte 0
await plc.WriteAsync("DB1.DBW4", (short)123); // Word at byte 4
```

---

## 4. Performance Strategy

### 4.1 High-Performance Async I/O

```
Goal: Maximize throughput when reading/writing many tags concurrently.
```

**Key techniques:**

1. **`ValueTask<T>` everywhere**: Avoid `Task` allocations for synchronous completions (cached values, pool hits).

2. **`System.IO.Pipelines`**: Use `PipeReader`/`PipeWriter` for TCP I/O instead of raw `NetworkStream`. This gives us:
   - Zero-copy buffer management
   - Automatic buffer pooling via `MemoryPool<byte>`
   - Efficient framing (read exactly N bytes without over-reading)
   - Backpressure support

3. **`System.Buffers.ArrayPool<byte>`**: All packet construction uses pooled buffers, never `new byte[]`.

4. **CIP Multiple Service Packet (0x0A)**: The most important performance feature. Instead of sending 50 individual read requests (50 round-trips), we pack them into Multiple Service Packets:
   - Each packet can hold as many services as fit in the CIP connection size (up to ~4002 bytes with Large Forward Open).
   - If 50 tags don't fit in one packet, we split into the minimum number of packets and send them concurrently.
   - This reduces 50 round-trips to 2-3.

5. **Connection multiplexing**: For scenarios with very high tag counts, support multiple CIP connections to the same PLC and distribute requests across them (pipeline parallelism).

6. **Tag database caching**: The tag list upload happens once on connect. Subsequent reads/writes use the cached metadata with zero allocation for type lookup.

### 4.2 Benchmark Targets

| Operation | libplctag.NET | SimplePLCDriverCore Target |
|-----------|--------------|---------------------------|
| Single tag read | ~5ms | ~3ms |
| 100 tags batch read | ~500ms (sequential) | ~15-30ms (batched) |
| 1000 tags batch read | ~5000ms | ~100-200ms |
| Tag list upload (500 tags) | N/A (manual) | <500ms |
| Memory per tag operation | ~1KB alloc | ~0 alloc (pooled) |

### 4.3 Buffer Management Detail

```csharp
// PacketWriter - stack-allocated for small packets, pooled for large
public ref struct PacketWriter
{
    private byte[] _buffer;
    private int _position;

    public PacketWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _position = 0;
    }

    public void WriteUInt16(ushort value) { /* little-endian */ }
    public void WriteUInt32(uint value) { /* little-endian */ }
    public void WriteBytes(ReadOnlySpan<byte> data) { /* memcpy */ }
    public void WriteSymbolicSegment(string tagName) { /* CIP 0x91 encoding */ }

    public ReadOnlyMemory<byte> GetWrittenMemory() => _buffer.AsMemory(0, _position);
    public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
}
```

---

## 5. Protocol Implementation Details

### 5.1 EtherNet/IP + CIP Stack (Phase 1 - Core)

This is the primary protocol stack for Allen-Bradley ControlLogix/CompactLogix.

#### Layer Architecture

```
+-------------------------------------------+
|          LogixDriver (user API)            |  Drivers/
|  - read(), write(), getTags(), getUdts()   |
+-------------------------------------------+
|         CIP Application Layer              |  Protocols/EtherNetIP/Cip/
|  - ReadTag(0x4C), WriteTag(0x4D)           |
|  - MultiService(0x0A), ForwardOpen(0x54)   |
|  - SymbolObject(0x6B), Template(0x6C)      |
+-------------------------------------------+
|       EtherNet/IP Encapsulation            |  Protocols/EtherNetIP/
|  - RegisterSession, SendRRData,            |
|    SendUnitData                            |
+-------------------------------------------+
|       TCP Transport (async)                |  Common/Transport/
|  - System.IO.Pipelines                     |
|  - Connection pooling                      |
+-------------------------------------------+
```

#### Key Protocol Constants

```csharp
// EtherNet/IP Commands
const ushort RegisterSession   = 0x0065;
const ushort UnregisterSession = 0x0066;
const ushort SendRRData        = 0x006F;  // Unconnected
const ushort SendUnitData      = 0x0070;  // Connected
const ushort ListIdentity      = 0x0063;

// CIP Services
const byte ReadTag             = 0x4C;
const byte WriteTag            = 0x4D;
const byte ReadTagFragmented   = 0x52;
const byte WriteTagFragmented  = 0x53;
const byte MultipleService     = 0x0A;
const byte ForwardOpen         = 0x54;
const byte LargeForwardOpen    = 0x5B;
const byte ForwardClose        = 0x4E;
const byte GetAttributeList    = 0x03;
const byte GetInstanceAttrList = 0x55;

// CIP Object Classes
const ushort IdentityObject    = 0x01;
const ushort ConnectionManager = 0x06;
const ushort SymbolObject      = 0x6B;
const ushort TemplateObject    = 0x6C;

// CIP Data Type Codes
const ushort CIP_BOOL  = 0x00C1;
const ushort CIP_SINT  = 0x00C2;
const ushort CIP_INT   = 0x00C3;
const ushort CIP_DINT  = 0x00C4;
const ushort CIP_LINT  = 0x00C5;
const ushort CIP_USINT = 0x00C6;
const ushort CIP_UINT  = 0x00C7;
const ushort CIP_UDINT = 0x00C8;
const ushort CIP_ULINT = 0x00C9;
const ushort CIP_REAL  = 0x00CA;
const ushort CIP_LREAL = 0x00CB;
const ushort CIP_STRING= 0x00DA;
// Bit 15 set = structure type, lower bits = template instance ID
```

#### Connection Flow

```
1. TCP Connect to port 44818
2. RegisterSession -> get session handle
3. Forward Open (or Large Forward Open for >504 byte packets)
   - Establishes CIP Class 3 connected transport
   - Negotiates connection size (target 4002 bytes)
4. Upload tag list via GetInstanceAttributeList on SymbolObject
5. Upload UDT templates via Template Object reads
6. Ready for read/write operations via SendUnitData
7. Forward Close on disconnect
8. UnregisterSession
9. TCP Close
```

### 5.2 PCCC over CIP (Phase 2 - SLC/MicroLogix/PLC5)

```
Same TCP+EtherNet/IP transport, but instead of Forward Open:
1. Use Unconnected Send (SendRRData)
2. CIP message wraps PCCC command to Class 0x67 (PCCC Object)
3. PCCC Protected Typed Logical Read (cmd 0x0F, fn 0x68)
4. PCCC Protected Typed Logical Write (cmd 0x0F, fn 0x67)
5. Address encoding: file_type + file_number + element + sub_element
```

### 5.3 S7comm (Phase 3 - Siemens)

```
1. TCP Connect to port 102
2. TPKT frame (4 bytes: version, reserved, length)
3. COTP Connection Request (CR) with TSAP (rack/slot encoding)
4. Wait for COTP Connection Confirm (CC)
5. S7 Communication Setup (negotiate PDU size, max connections)
6. S7 Read/Write requests within S7comm PDUs
   - Supports multi-item requests (like CIP Multiple Service)
   - Big-endian byte order
   - Area codes: 0x81=I, 0x82=Q, 0x83=M, 0x84=DB
   - Addressing: byte offset * 8 + bit number
```

### 5.4 FINS (Phase 3 - Omron)

```
1. TCP Connect to port 9600
2. FINS/TCP handshake (node address exchange)
3. FINS header (10 bytes) + command frame
4. Memory Area Read (0x0101) / Write (0x0102)
   - Area codes: 0x82=HR, 0xB0=DM, etc.
   - Big-endian byte order
```

---

## 6. CIP Data Type Codec System

### 6.1 Supported PLC Data Types -> .NET Mapping

| CIP Type | Code | .NET Type | Size | Notes |
|----------|------|-----------|------|-------|
| BOOL | 0xC1 | `bool` | 1 byte* | *Packed in DINT for Logix arrays |
| SINT | 0xC2 | `sbyte` | 1 byte | |
| INT | 0xC3 | `short` | 2 bytes | |
| DINT | 0xC4 | `int` | 4 bytes | |
| LINT | 0xC5 | `long` | 8 bytes | |
| USINT | 0xC6 | `byte` | 1 byte | |
| UINT | 0xC7 | `ushort` | 2 bytes | |
| UDINT | 0xC8 | `uint` | 4 bytes | |
| ULINT | 0xC9 | `ulong` | 8 bytes | |
| REAL | 0xCA | `float` | 4 bytes | |
| LREAL | 0xCB | `double` | 8 bytes | |
| STRING | 0xDA | `string` | 88 bytes | Logix STRING (4-byte len + 82 chars + 2 pad) |
| Structure | 0x8xxx | `Dictionary<string, PlcValue>` | Variable | Template instance ID in lower bits |

### 6.2 Type Auto-Detection Flow

```
User calls: plc.ReadAsync("MyTag")
                    |
                    v
    [1] Lookup "MyTag" in cached tag database
         -> found: type = 0x00C4 (DINT)
                    |
                    v
    [2] Build CIP ReadTag(0x4C) with symbolic path "MyTag"
                    |
                    v
    [3] Send via Multiple Service Packet (if batched)
                    |
                    v
    [4] Receive response bytes
                    |
                    v
    [5] Decode using known type (0xC4 -> ReadInt32 little-endian)
                    |
                    v
    [6] Wrap in PlcValue(value: 42, dataType: PlcDataType.DINT)
                    |
                    v
    [7] Return TagResult(tagName: "MyTag", value: PlcValue, typeName: "DINT")
```

For UDTs:
```
    [5] Type is 0x8xxx (structure)
         -> look up template instance in UDT cache
         -> get member definitions (name, offset, type)
         -> decode each member recursively
         -> build Dictionary<string, PlcValue>
         -> wrap in PlcValue
```

---

## 7. Phased Implementation Plan

### Phase 1: Core Foundation + ControlLogix/CompactLogix (Weeks 1-6)

**Goal**: Full read/write support for ControlLogix/CompactLogix with typeless access and batch operations.

#### Week 1-2: Transport & EtherNet/IP Layer
- [ ] Project scaffolding (.sln, .csproj, folder structure)
- [ ] `TcpTransport` - async TCP with `System.IO.Pipelines`
- [ ] `EipEncapsulation` - 24-byte header encode/decode
- [ ] `EipSession` - RegisterSession/UnregisterSession
- [ ] `PacketReader`/`PacketWriter` - binary serialization helpers
- [ ] Unit tests for all packet encoding/decoding

#### Week 3: CIP Connection Management
- [ ] `CipPath` - symbolic segment encoding, route path building
- [ ] `ForwardOpen` / `LargeForwardOpen` / `ForwardClose`
- [ ] Connected messaging (SendUnitData) with sequence tracking
- [ ] Unconnected messaging (SendRRData) support
- [ ] Connection timeout and keepalive handling

#### Week 4: Tag Operations
- [ ] `ReadTag` (0x4C) and `WriteTag` (0x4D) single tag ops
- [ ] `ReadTagFragmented` (0x52) / `WriteTagFragmented` (0x53)
- [ ] `MultipleServicePacket` (0x0A) - batch request builder
- [ ] Auto-splitting oversized batch requests
- [ ] CIP data type codec (all atomic types)
- [ ] Array read/write support

#### Week 5: Metadata Discovery & Type System
- [ ] `SymbolObject` - tag list upload (Class 0x6B)
- [ ] `TemplateObject` - UDT definition reading (Class 0x6C)
- [ ] `DataTypeRegistry` - cache tag types and UDT definitions
- [ ] `PlcValue` - universal value wrapper with conversions
- [ ] `StructureDecoder` / `StructureEncoder` for UDTs
- [ ] Program-scoped tag support
- [ ] String type handling

#### Week 6: LogixDriver Integration & Testing
- [ ] `LogixDriver` - high-level API combining all above
- [ ] `IPlcDriver` and `ITagBrowser` implementation
- [ ] `PlcDriverFactory` - driver creation
- [ ] Error handling and CIP status code mapping
- [ ] Reconnection logic
- [ ] Integration tests with real ControlLogix/CompactLogix PLC
- [ ] Sample applications
- [ ] NuGet package setup

### Phase 2: SLC/MicroLogix/PLC-5 Support (Weeks 7-9)

- [ ] `PcccAddress` - SLC address parser (N7:0, F8:1, B3:0/5, etc.)
- [ ] `PcccCommand` - PCCC frame builder (Protected Typed Logical Read/Write)
- [ ] PCCC data type mapping
- [ ] `SlcDriver` - high-level API
- [ ] MicroLogix variant handling
- [ ] PLC-5 variant handling
- [ ] Integration tests with real SLC/MicroLogix hardware

### Phase 3: Siemens & Omron (Weeks 10-14)

#### Siemens S7 (Weeks 10-12)
- [ ] `TpktPacket` / `CotpPacket` - ISO transport framing
- [ ] `S7Session` - COTP+S7 connection handshake
- [ ] `S7Message` - read/write PDU builder
- [ ] `S7Address` - DB.DBx addressing parser
- [ ] `S7Types` - data type codec (big-endian!)
- [ ] `SiemensDriver` - high-level API
- [ ] Multi-item request support (S7's batch mechanism)
- [ ] Integration tests with S7-1200/1500

#### Omron FINS (Weeks 13-14)
- [ ] `FinsSession` - FINS/TCP handshake
- [ ] `FinsMessage` - memory area read/write
- [ ] `FinsAddress` - area/address parsing
- [ ] `OmronDriver` - high-level API
- [ ] Integration tests with Omron NJ/NX

### Phase 4: Modbus & Polish (Weeks 15-16)

- [ ] `ModbusSession` - MBAP framing
- [ ] `ModbusMessage` - function codes 0x01-0x10
- [ ] `ModbusDriver` - high-level API
- [ ] Performance benchmarking and optimization
- [ ] Documentation
- [ ] CI/CD pipeline
- [ ] NuGet publish

---

## 8. Technical Decisions & Trade-offs

### 8.1 Why Pure .NET (No Native Dependencies)?

| Concern | Decision | Rationale |
|---------|----------|-----------|
| Performance | Pure .NET with Span/Pipelines | Modern .NET I/O is competitive with C; P/Invoke overhead eliminated |
| Deployment | xcopy / NuGet only | No native DLL management, no platform-specific binaries |
| Debugging | Full .NET stack traces | No mixed-mode debugging needed |
| AOT/Trimming | Full compatibility | No unmanaged code blocks NativeAOT |
| Cross-platform | .NET handles it | No need to compile C for each RID |

### 8.2 Why `PlcValue` struct instead of generics?

**Option A (libplctag.NET style)**: `Tag<int>`, `Tag<float>` - user must know type at compile time.

**Option B (our approach)**: `PlcValue` wraps any type, with safe conversion methods.

**Rationale**: The whole point is that the user shouldn't need to know the PLC tag type. The driver discovers it automatically. If we forced `Tag<T>`, the user would need to specify `T` - defeating the purpose. `PlcValue` with implicit conversions gives a clean API:

```csharp
int x = (await plc.ReadAsync("SomeDint")).Value;  // implicit conversion
// User doesn't need to know it's a DINT - it just works
```

### 8.3 Why `ValueTask` instead of `Task`?

Most tag operations complete synchronously when data is already buffered. `ValueTask<T>` avoids allocating a `Task` object on the hot path, reducing GC pressure in high-frequency polling scenarios.

### 8.4 Connection Pooling Strategy

For applications monitoring hundreds of tags across multiple PLCs:

```csharp
// Connection pool manages PLC connections
services.AddSimplePlcDriver(options =>
{
    options.AddLogix("PLC1", "192.168.1.100");
    options.AddLogix("PLC2", "192.168.1.101");
    options.AddSiemens("PLC3", "192.168.1.200", rack: 0, slot: 1);
    options.ConnectionTimeout = TimeSpan.FromSeconds(5);
    options.AutoReconnect = true;
});
```

### 8.5 Target Framework

- **Minimum**: .NET 8.0 (LTS, modern APIs, good Span/Pipelines support)
- **Recommended**: .NET 8.0+ (latest LTS)
- Reason: Need `Span<T>`, `Memory<T>`, `System.IO.Pipelines`, `ValueTask`, `IAsyncDisposable` which are all mature in .NET 8.

---

## 9. Testing Strategy

### 9.1 Unit Tests (No PLC Required)

- **Packet encoding/decoding**: Verify every protocol packet is correctly serialized/deserialized using known byte sequences from protocol specs and Wireshark captures.
- **CIP path encoding**: Test symbolic segment encoding for various tag name patterns.
- **Data type codec**: Test every CIP type code encode/decode round-trip.
- **SLC address parser**: Test all address formats (N7:0, F8:1, B3:0/5, ST9:0, etc.)
- **PlcValue conversions**: Test all implicit and explicit conversion paths.
- **Multiple Service Packet splitting**: Verify correct splitting when batch exceeds connection size.
- **UDT decode**: Test structure decoding with mock template definitions.

### 9.2 Integration Tests (Real PLC)

- Marked with `[Category("Integration")]` and skipped in CI unless a PLC is available.
- Test matrix: ControlLogix, CompactLogix, SLC, MicroLogix, Siemens S7-1200, etc.
- Test scenarios: single read, single write, batch read/write, UDT read/write, tag browsing, connection loss recovery.

### 9.3 Protocol Conformance

- Capture real PLC communication with Wireshark
- Create byte-level test fixtures from captures
- Validate our packets match expected byte sequences

---

## 10. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| CIP protocol complexity | High | Implement incrementally; use pycomm3 as reference; validate with Wireshark captures |
| PLC hardware for testing | Medium | Use ControlLogix emulator (RSLogix Emulate); partner with users who have hardware |
| S7comm undocumented areas | Medium | Reference Snap7 (open-source C library) and Wireshark S7comm dissector |
| Performance regression | Medium | Benchmark suite from day 1; compare against libplctag.NET |
| Packet size edge cases | Low | Extensive unit tests for fragmentation and splitting boundaries |

---

## 11. Dependencies

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core library has ZERO external dependencies -->
    <!-- Only uses BCL: System.IO.Pipelines, System.Buffers, System.Net.Sockets -->
  </ItemGroup>
</Project>
```

**Zero external NuGet dependencies** for the core library. Only BCL packages that ship with .NET 8:
- `System.IO.Pipelines` (in-box since .NET 8)
- `System.Buffers` (in-box)
- `System.Net.Sockets` (in-box)
- `System.Threading.Channels` (for internal message queuing)

The optional `SimplePLCDriverCore.Extensions` package would depend on:
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Diagnostics.HealthChecks`

---

## 12. License

Recommend **MIT License** - permissive, matches pycomm3 and most .NET open-source libraries. No viral licensing concerns for commercial/industrial use.

---

## 13. Summary

SimplePLCDriverCore fills a clear gap in the .NET ecosystem: a native, async-first, typeless PLC communication library with rich metadata discovery. The phased approach starts with the most important target (Allen-Bradley ControlLogix/CompactLogix) and expands to other PLC families. The architecture draws heavily from pycomm3's excellent developer experience while leveraging modern .NET performance primitives (`Span<T>`, `Pipelines`, `ValueTask`) for industrial-grade throughput.
