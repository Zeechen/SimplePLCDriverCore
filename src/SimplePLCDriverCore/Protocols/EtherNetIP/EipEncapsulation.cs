using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP;

/// <summary>
/// EtherNet/IP Encapsulation Header (24 bytes):
///
///   Offset  Size  Field
///   ------  ----  -----
///   0       2     Command (ushort LE)
///   2       2     Length (ushort LE) - length of data following header
///   4       4     Session Handle (uint LE)
///   8       4     Status (uint LE)
///   12      8     Sender Context (8 bytes, echoed back)
///   20      4     Options (uint LE, always 0)
///
/// All fields are little-endian.
/// </summary>
internal readonly struct EipEncapsulationHeader
{
    public EipCommand Command { get; }
    public ushort DataLength { get; }
    public uint SessionHandle { get; }
    public EipStatus Status { get; }
    public ReadOnlyMemory<byte> SenderContext { get; }
    public uint Options { get; }

    public EipEncapsulationHeader(
        EipCommand command,
        ushort dataLength,
        uint sessionHandle,
        EipStatus status,
        ReadOnlyMemory<byte> senderContext,
        uint options = 0)
    {
        Command = command;
        DataLength = dataLength;
        SessionHandle = sessionHandle;
        Status = status;
        SenderContext = senderContext;
        Options = options;
    }

    /// <summary>Total message size (header + data payload).</summary>
    public int TotalLength => EipConstants.EncapsulationHeaderSize + DataLength;
}

/// <summary>
/// Encodes and decodes EtherNet/IP encapsulation messages.
/// </summary>
internal static class EipEncapsulation
{
    private static readonly byte[] EmptySenderContext = new byte[EipConstants.SenderContextSize];
    private static ulong _contextCounter;

    /// <summary>
    /// Extract the total message length from a 24-byte encapsulation header.
    /// Used with ITransport.ReceiveFramedAsync for protocol framing.
    /// </summary>
    public static int GetTotalLengthFromHeader(byte[] header)
    {
        var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        return EipConstants.EncapsulationHeaderSize + dataLength;
    }

    /// <summary>Decode a 24-byte encapsulation header.</summary>
    public static EipEncapsulationHeader DecodeHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < EipConstants.EncapsulationHeaderSize)
            throw new InvalidDataException(
                $"EIP header requires {EipConstants.EncapsulationHeaderSize} bytes, got {data.Length}");

        return new EipEncapsulationHeader(
            command: (EipCommand)BinaryPrimitives.ReadUInt16LittleEndian(data),
            dataLength: BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
            sessionHandle: BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            status: (EipStatus)BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            senderContext: data.Slice(12, 8).ToArray(),
            options: BinaryPrimitives.ReadUInt32LittleEndian(data[20..])
        );
    }

    /// <summary>Decode a full encapsulation message (header + data payload).</summary>
    public static (EipEncapsulationHeader Header, ReadOnlyMemory<byte> Data) Decode(byte[] message)
    {
        var header = DecodeHeader(message.AsSpan());

        ReadOnlyMemory<byte> data = header.DataLength > 0
            ? message.AsMemory(EipConstants.EncapsulationHeaderSize, header.DataLength)
            : ReadOnlyMemory<byte>.Empty;

        return (header, data);
    }

    /// <summary>
    /// Build a RegisterSession request (command 0x0065).
    /// Data payload: protocol version (2 bytes) + option flags (2 bytes) = 4 bytes.
    /// </summary>
    public static byte[] BuildRegisterSession()
    {
        using var writer = new PacketWriter(EipConstants.EncapsulationHeaderSize + 4);

        // Header
        WriteHeader(writer, EipCommand.RegisterSession, dataLength: 4, sessionHandle: 0);

        // Data: protocol version + options
        writer.WriteUInt16LE(EipConstants.ProtocolVersion);
        writer.WriteUInt16LE(0); // options flags

        return writer.ToArray();
    }

    /// <summary>
    /// Build an UnregisterSession request (command 0x0066).
    /// No data payload.
    /// </summary>
    public static byte[] BuildUnregisterSession(uint sessionHandle)
    {
        using var writer = new PacketWriter(EipConstants.EncapsulationHeaderSize);
        WriteHeader(writer, EipCommand.UnregisterSession, dataLength: 0, sessionHandle: sessionHandle);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a SendRRData request (command 0x006F) for unconnected messaging.
    /// Used for Forward Open/Close and discovery operations.
    ///
    /// Data format:
    ///   Interface Handle (4 bytes) = 0
    ///   Timeout (2 bytes)
    ///   CPF: Item Count (2 bytes) = 2
    ///     Item 1: Null Address (type=0x0000, length=0)
    ///     Item 2: Unconnected Data (type=0x00B2, length=N, data=CIP message)
    /// </summary>
    public static byte[] BuildSendRRData(
        uint sessionHandle,
        ReadOnlySpan<byte> cipMessage,
        ushort timeout = 10)
    {
        // Data payload size: 4 (interface) + 2 (timeout) + 2 (item count)
        //   + 4 (null address item) + 4 (unconnected data item header) + cipMessage.Length
        var dataLength = 4 + 2 + 2 + 4 + 4 + cipMessage.Length;

        using var writer = new PacketWriter(EipConstants.EncapsulationHeaderSize + dataLength);

        // Header
        WriteHeader(writer, EipCommand.SendRRData, (ushort)dataLength, sessionHandle);

        // Interface Handle (CIP = 0)
        writer.WriteUInt32LE(0);
        // Timeout
        writer.WriteUInt16LE(timeout);

        // CPF - 2 items
        writer.WriteUInt16LE(2); // item count

        // Item 1: Null Address
        writer.WriteUInt16LE((ushort)CpfItemTypeId.NullAddress);
        writer.WriteUInt16LE(0); // length

        // Item 2: Unconnected Data
        writer.WriteUInt16LE((ushort)CpfItemTypeId.UnconnectedData);
        writer.WriteUInt16LE((ushort)cipMessage.Length);
        writer.WriteBytes(cipMessage);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a SendUnitData request (command 0x0070) for connected messaging.
    /// Used for read/write operations after Forward Open.
    ///
    /// Data format:
    ///   Interface Handle (4 bytes) = 0
    ///   Timeout (2 bytes) = 0 (connected messages don't timeout at EIP level)
    ///   CPF: Item Count (2 bytes) = 2
    ///     Item 1: Connected Address (type=0x00A1, length=4, connectionId)
    ///     Item 2: Connected Data (type=0x00B1, length=N+2, sequenceNumber + cipMessage)
    /// </summary>
    public static byte[] BuildSendUnitData(
        uint sessionHandle,
        uint connectionId,
        ushort sequenceNumber,
        ReadOnlySpan<byte> cipMessage)
    {
        // Data payload size: 4 (interface) + 2 (timeout) + 2 (item count)
        //   + 4 (connected address header) + 4 (connection id)
        //   + 4 (connected data header) + 2 (sequence) + cipMessage.Length
        var dataLength = 4 + 2 + 2 + 4 + 4 + 4 + 2 + cipMessage.Length;

        using var writer = new PacketWriter(EipConstants.EncapsulationHeaderSize + dataLength);

        // Header
        WriteHeader(writer, EipCommand.SendUnitData, (ushort)dataLength, sessionHandle);

        // Interface Handle (CIP = 0)
        writer.WriteUInt32LE(0);
        // Timeout (0 for connected)
        writer.WriteUInt16LE(0);

        // CPF - 2 items
        writer.WriteUInt16LE(2);

        // Item 1: Connected Address
        writer.WriteUInt16LE((ushort)CpfItemTypeId.ConnectedAddress);
        writer.WriteUInt16LE(4); // length
        writer.WriteUInt32LE(connectionId);

        // Item 2: Connected Data
        writer.WriteUInt16LE((ushort)CpfItemTypeId.ConnectedData);
        writer.WriteUInt16LE((ushort)(cipMessage.Length + 2)); // +2 for sequence number
        writer.WriteUInt16LE(sequenceNumber);
        writer.WriteBytes(cipMessage);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a ListIdentity request (command 0x0063) for PLC discovery.
    /// No data payload.
    /// </summary>
    public static byte[] BuildListIdentity()
    {
        using var writer = new PacketWriter(EipConstants.EncapsulationHeaderSize);
        WriteHeader(writer, EipCommand.ListIdentity, dataLength: 0, sessionHandle: 0);
        return writer.ToArray();
    }

    /// <summary>
    /// Parse a SendRRData or SendUnitData response to extract the CIP message data.
    /// Skips the interface handle, timeout, and CPF wrapper.
    /// </summary>
    public static ReadOnlyMemory<byte> ExtractCipData(
        ReadOnlyMemory<byte> eipData, bool isConnected = false)
    {
        var reader = new PacketReader(eipData);

        // Skip interface handle (4) + timeout (2)
        reader.Skip(6);

        // CPF item count
        var itemCount = reader.ReadUInt16LE();
        if (itemCount < 2)
            throw new InvalidDataException($"Expected at least 2 CPF items, got {itemCount}");

        // Item 1: address item (skip it)
        var addressType = reader.ReadUInt16LE();
        var addressLength = reader.ReadUInt16LE();
        reader.Skip(addressLength);

        // Item 2: data item
        var dataType = reader.ReadUInt16LE();
        var dataLength = reader.ReadUInt16LE();

        if (isConnected)
        {
            // Connected data has a 2-byte sequence number prefix
            reader.Skip(2);
            dataLength -= 2;
        }

        var offset = reader.Position;
        return eipData.Slice(offset, dataLength);
    }

    /// <summary>Generate a unique sender context for request/response correlation.</summary>
    public static byte[] GenerateSenderContext()
    {
        var context = new byte[EipConstants.SenderContextSize];
        var counter = Interlocked.Increment(ref _contextCounter);
        BinaryPrimitives.WriteUInt64LittleEndian(context, counter);
        return context;
    }

    // --- Private Helpers ---

    private static void WriteHeader(
        PacketWriter writer,
        EipCommand command,
        ushort dataLength,
        uint sessionHandle)
    {
        var context = GenerateSenderContext();

        writer.WriteUInt16LE((ushort)command);    // Command
        writer.WriteUInt16LE(dataLength);          // Length
        writer.WriteUInt32LE(sessionHandle);       // Session Handle
        writer.WriteUInt32LE(0);                   // Status (0 for requests)
        writer.WriteBytes(context);                // Sender Context
        writer.WriteUInt32LE(0);                   // Options
    }
}
