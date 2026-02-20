using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// Parsed response from a ForwardOpen request.
/// </summary>
internal readonly struct ForwardOpenResult
{
    public uint OtoTConnectionId { get; init; }
    public uint TtoOConnectionId { get; init; }
    public ushort ConnectionSerialNumber { get; init; }
    public ushort OriginatorVendorId { get; init; }
    public uint OriginatorSerialNumber { get; init; }
    public uint OtoTActualPacketRate { get; init; }
    public uint TtoOActualPacketRate { get; init; }
}

/// <summary>
/// Builds Forward Open and Forward Close CIP messages for establishing
/// and tearing down CIP Class 3 connected transport.
/// </summary>
internal static class ForwardOpen
{
    // Connection parameters
    private const uint DefaultRpi = 2_000_000;  // 2 seconds in microseconds
    private const ushort DefaultConnectionSize = 4002; // Large Forward Open max
    private const ushort SmallConnectionSize = 504;    // Standard Forward Open max
    private const byte PriorityTimeTick = 10;
    private const byte TimeoutTicks = 240;

    // Used to generate unique connection serial numbers
    private static int _serialNumber;

    /// <summary>
    /// Build a Large Forward Open request (service 0x5B) for connection sizes up to 4002 bytes.
    /// This is preferred for ControlLogix/CompactLogix as it allows larger batch operations.
    /// </summary>
    public static (byte[] Request, ushort SerialNumber) BuildLargeForwardOpen(
        byte slot,
        uint originatorSerialNumber,
        ushort vendorId = 0x0001,
        uint connectionSize = DefaultConnectionSize)
    {
        var serialNumber = Interlocked.Increment(ref _serialNumber);
        var connSerialNumber = (ushort)serialNumber;

        // Path to Connection Manager
        var cmPath = CipPath.BuildClassInstancePath(CipClasses.ConnectionManager, 1);
        var routePath = CipPath.EncodeRoutePath(slot);

        using var writer = new PacketWriter(128);

        // Service
        writer.WriteUInt8(CipServices.LargeForwardOpen);

        // Path to Connection Manager
        writer.WriteUInt8(CipPath.GetPathSizeInWords(cmPath.Length));
        writer.WriteBytes(cmPath);

        // Priority/Time Tick
        writer.WriteUInt8(PriorityTimeTick);
        writer.WriteUInt8(TimeoutTicks);

        // O->T Connection ID (0 = PLC assigns)
        writer.WriteUInt32LE(0);
        // T->O Connection ID (0 = PLC assigns)
        writer.WriteUInt32LE(0);

        // Connection Serial Number
        writer.WriteUInt16LE(connSerialNumber);
        // Originator Vendor ID
        writer.WriteUInt16LE(vendorId);
        // Originator Serial Number
        writer.WriteUInt32LE(originatorSerialNumber);

        // Connection Timeout Multiplier
        writer.WriteUInt8(3); // multiply by 8

        // Reserved (3 bytes)
        writer.WriteZeros(3);

        // O->T RPI (Requested Packet Interval) in microseconds
        writer.WriteUInt32LE(DefaultRpi);
        // O->T Network Connection Parameters (Large Forward Open uses 32-bit)
        // Bits: [31]=redundant owner, [30-29]=connection type, [28-26]=priority,
        //       [25]=fixed/variable, [24-16]=reserved(0), [15-0]=connection size
        writer.WriteUInt32LE(0x42000000 | connectionSize); // P2P, low priority, variable

        // T->O RPI
        writer.WriteUInt32LE(DefaultRpi);
        // T->O Network Connection Parameters
        writer.WriteUInt32LE(0x42000000 | connectionSize);

        // Transport Type/Trigger
        // Class 3, application trigger, server
        writer.WriteUInt8(0xA3);

        // Connection Path
        var connectionPathBytes = BuildConnectionPath(slot);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(connectionPathBytes.Length));
        writer.WriteBytes(connectionPathBytes);

        return (writer.ToArray(), connSerialNumber);
    }

    /// <summary>
    /// Build a standard Forward Open request (service 0x54) for connection sizes up to 504 bytes.
    /// Used as fallback if Large Forward Open is not supported.
    /// </summary>
    public static (byte[] Request, ushort SerialNumber) BuildForwardOpen(
        byte slot,
        uint originatorSerialNumber,
        ushort vendorId = 0x0001,
        ushort connectionSize = SmallConnectionSize)
    {
        var serialNumber = Interlocked.Increment(ref _serialNumber);
        var connSerialNumber = (ushort)serialNumber;

        var cmPath = CipPath.BuildClassInstancePath(CipClasses.ConnectionManager, 1);

        using var writer = new PacketWriter(96);

        // Service
        writer.WriteUInt8(CipServices.ForwardOpen);

        // Path to Connection Manager
        writer.WriteUInt8(CipPath.GetPathSizeInWords(cmPath.Length));
        writer.WriteBytes(cmPath);

        // Priority/Time Tick
        writer.WriteUInt8(PriorityTimeTick);
        writer.WriteUInt8(TimeoutTicks);

        // O->T Connection ID
        writer.WriteUInt32LE(0);
        // T->O Connection ID
        writer.WriteUInt32LE(0);

        // Connection Serial Number
        writer.WriteUInt16LE(connSerialNumber);
        // Originator Vendor ID
        writer.WriteUInt16LE(vendorId);
        // Originator Serial Number
        writer.WriteUInt32LE(originatorSerialNumber);

        // Connection Timeout Multiplier
        writer.WriteUInt8(3);

        // Reserved (3 bytes)
        writer.WriteZeros(3);

        // O->T RPI
        writer.WriteUInt32LE(DefaultRpi);
        // O->T Network Connection Parameters (Standard uses 16-bit)
        // Bits: [15]=redundant owner, [14:13]=type, [12:10]=priority, [9]=fixed/variable, [8:0]=size
        writer.WriteUInt16LE((ushort)(0x43E0 | (connectionSize & 0x01FF)));

        // T->O RPI
        writer.WriteUInt32LE(DefaultRpi);
        // T->O Network Connection Parameters
        writer.WriteUInt16LE((ushort)(0x43E0 | (connectionSize & 0x01FF)));

        // Transport Type/Trigger
        writer.WriteUInt8(0xA3);

        // Connection Path
        var connectionPathBytes = BuildConnectionPath(slot);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(connectionPathBytes.Length));
        writer.WriteBytes(connectionPathBytes);

        return (writer.ToArray(), connSerialNumber);
    }

    /// <summary>
    /// Build a Forward Close request (service 0x4E) to tear down a CIP connection.
    /// </summary>
    public static byte[] BuildForwardClose(
        ushort connectionSerialNumber,
        ushort vendorId,
        uint originatorSerialNumber,
        byte slot)
    {
        var cmPath = CipPath.BuildClassInstancePath(CipClasses.ConnectionManager, 1);

        using var writer = new PacketWriter(48);

        // Service
        writer.WriteUInt8(CipServices.ForwardClose);

        // Path to Connection Manager
        writer.WriteUInt8(CipPath.GetPathSizeInWords(cmPath.Length));
        writer.WriteBytes(cmPath);

        // Priority/Time Tick
        writer.WriteUInt8(PriorityTimeTick);
        writer.WriteUInt8(TimeoutTicks);

        // Connection Serial Number
        writer.WriteUInt16LE(connectionSerialNumber);
        // Originator Vendor ID
        writer.WriteUInt16LE(vendorId);
        // Originator Serial Number
        writer.WriteUInt32LE(originatorSerialNumber);

        // Connection Path
        var connectionPathBytes = BuildConnectionPath(slot);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(connectionPathBytes.Length));
        writer.WriteUInt8(0); // reserved
        writer.WriteBytes(connectionPathBytes);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse a Forward Open response to extract connection IDs.
    /// </summary>
    public static ForwardOpenResult ParseResponse(ReadOnlyMemory<byte> data)
    {
        var reader = new PacketReader(data);

        return new ForwardOpenResult
        {
            OtoTConnectionId = reader.ReadUInt32LE(),
            TtoOConnectionId = reader.ReadUInt32LE(),
            ConnectionSerialNumber = reader.ReadUInt16LE(),
            OriginatorVendorId = reader.ReadUInt16LE(),
            OriginatorSerialNumber = reader.ReadUInt32LE(),
            OtoTActualPacketRate = reader.ReadUInt32LE(),
            TtoOActualPacketRate = reader.ReadUInt32LE(),
        };
    }

    /// <summary>
    /// Build the connection path: backplane port + slot -> backplane port + Message Router.
    /// </summary>
    private static byte[] BuildConnectionPath(byte slot)
    {
        using var writer = new PacketWriter(12);

        // Port 1 (backplane), link address = slot
        writer.WriteUInt8(0x01); // port segment: port 1
        writer.WriteUInt8(slot); // slot number

        // Address to Message Router: class 0x02, instance 1
        CipPath.EncodeClassSegment(writer, CipClasses.MessageRouter);
        CipPath.EncodeInstanceSegment(writer, 1);

        return writer.ToArray();
    }
}
