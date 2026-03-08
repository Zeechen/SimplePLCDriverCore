using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.S7;

/// <summary>
/// S7comm message types.
/// </summary>
internal enum S7MessageType : byte
{
    Job = 0x01,
    Ack = 0x02,
    AckData = 0x03,
    UserData = 0x07,
}

/// <summary>
/// S7comm function codes.
/// </summary>
internal enum S7Function : byte
{
    CommunicationSetup = 0xF0,
    ReadVar = 0x04,
    WriteVar = 0x05,
}

/// <summary>
/// Parsed S7 response.
/// </summary>
internal readonly struct S7Response
{
    public bool IsSuccess { get; }
    public byte ErrorClass { get; }
    public byte ErrorCode { get; }
    public byte[][] ItemData { get; }
    public byte[] ItemReturnCodes { get; }

    public S7Response(bool isSuccess, byte errorClass, byte errorCode,
        byte[][] itemData, byte[] itemReturnCodes)
    {
        IsSuccess = isSuccess;
        ErrorClass = errorClass;
        ErrorCode = errorCode;
        ItemData = itemData;
        ItemReturnCodes = itemReturnCodes;
    }

    public string GetErrorMessage()
    {
        if (IsSuccess)
            return string.Empty;

        return $"S7 error: class=0x{ErrorClass:X2}, code=0x{ErrorCode:X2}" +
               $" ({GetErrorDescription(ErrorClass, ErrorCode)})";
    }

    public static string GetItemErrorMessage(byte returnCode) => returnCode switch
    {
        0xFF => "Success",
        0x01 => "Hardware error",
        0x03 => "Object access not allowed",
        0x05 => "Invalid address",
        0x06 => "Data type not supported",
        0x07 => "Data type inconsistent",
        0x0A => "Object does not exist",
        _ => $"Unknown error (0x{returnCode:X2})"
    };

    private static string GetErrorDescription(byte errorClass, byte errorCode)
    {
        return errorClass switch
        {
            0x00 => "No error",
            0x81 => "Application relationship error",
            0x82 => "Object definition error",
            0x83 => "No resources available",
            0x84 => "Service processing error",
            0x85 => "Supply error",
            0x87 => "Access error",
            _ => $"Unknown (class=0x{errorClass:X2}, code=0x{errorCode:X2})"
        };
    }
}

/// <summary>
/// S7comm message builder and parser.
///
/// S7comm header (10 or 12 bytes):
///   byte 0: Protocol ID (always 0x32)
///   byte 1: Message type (Job=0x01, AckData=0x03)
///   byte 2-3: Reserved (0x0000)
///   byte 4-5: PDU reference (sequence number, big-endian)
///   byte 6-7: Parameter length (big-endian)
///   byte 8-9: Data length (big-endian)
///   For AckData responses:
///     byte 10: Error class
///     byte 11: Error code
/// </summary>
internal static class S7Message
{
    public const byte ProtocolId = 0x32;
    public const int JobHeaderSize = 10;
    public const int AckDataHeaderSize = 12;

    /// <summary>
    /// Build an S7 Communication Setup request.
    /// Negotiates PDU size and max connections.
    /// </summary>
    public static byte[] BuildSetupCommunication(ushort pduReference,
        ushort maxAmqCalling = 1, ushort maxAmqCalled = 1, ushort pduSize = 480)
    {
        using var writer = new PacketWriter(32);

        // S7 header (Job)
        writer.WriteUInt8(ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.Job);
        writer.WriteUInt16BE(0); // reserved
        writer.WriteUInt16BE(pduReference);
        writer.WriteUInt16BE(8); // parameter length
        writer.WriteUInt16BE(0); // data length

        // Parameter: Communication Setup
        writer.WriteUInt8((byte)S7Function.CommunicationSetup);
        writer.WriteUInt8(0); // reserved
        writer.WriteUInt16BE(maxAmqCalling);
        writer.WriteUInt16BE(maxAmqCalled);
        writer.WriteUInt16BE(pduSize);

        return writer.ToArray();
    }

    /// <summary>
    /// Build an S7 Read Variable request for one or more items.
    /// </summary>
    public static byte[] BuildReadRequest(ushort pduReference, S7Address[] addresses)
    {
        using var writer = new PacketWriter(128);

        // Build parameter section first
        using var paramWriter = new PacketWriter(64);
        paramWriter.WriteUInt8((byte)S7Function.ReadVar);
        paramWriter.WriteUInt8((byte)addresses.Length); // item count

        foreach (var addr in addresses)
            WriteReadItemSpec(paramWriter, addr);

        var paramData = paramWriter.GetWrittenSpan();

        // S7 header
        writer.WriteUInt8(ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.Job);
        writer.WriteUInt16BE(0); // reserved
        writer.WriteUInt16BE(pduReference);
        writer.WriteUInt16BE((ushort)paramData.Length); // parameter length
        writer.WriteUInt16BE(0); // data length (no data for reads)

        writer.WriteBytes(paramData);

        return writer.ToArray();
    }

    /// <summary>
    /// Build an S7 Write Variable request for a single item.
    /// </summary>
    public static byte[] BuildWriteRequest(ushort pduReference, S7Address address, byte[] data)
    {
        using var writer = new PacketWriter(128);

        // Parameter section
        using var paramWriter = new PacketWriter(32);
        paramWriter.WriteUInt8((byte)S7Function.WriteVar);
        paramWriter.WriteUInt8(1); // item count
        WriteReadItemSpec(paramWriter, address);
        var paramData = paramWriter.GetWrittenSpan();

        // Data section
        using var dataWriter = new PacketWriter(64);
        WriteDataItem(dataWriter, address, data);
        var dataSection = dataWriter.GetWrittenSpan();

        // S7 header
        writer.WriteUInt8(ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.Job);
        writer.WriteUInt16BE(0); // reserved
        writer.WriteUInt16BE(pduReference);
        writer.WriteUInt16BE((ushort)paramData.Length);
        writer.WriteUInt16BE((ushort)dataSection.Length);

        writer.WriteBytes(paramData);
        writer.WriteBytes(dataSection);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse an S7 response (AckData message).
    /// </summary>
    public static S7Response ParseResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < AckDataHeaderSize)
            throw new InvalidOperationException($"S7 response too short: {data.Length} bytes.");

        if (data[0] != ProtocolId)
            throw new InvalidOperationException($"Invalid S7 protocol ID: 0x{data[0]:X2}.");

        var msgType = (S7MessageType)data[1];
        if (msgType != S7MessageType.AckData)
            throw new InvalidOperationException($"Expected AckData (0x03), got 0x{data[1]:X2}.");

        var paramLength = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        var dataLength = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        var errorClass = data[10];
        var errorCode = data[11];

        if (errorClass != 0)
        {
            return new S7Response(false, errorClass, errorCode,
                Array.Empty<byte[]>(), Array.Empty<byte>());
        }

        // Parse parameter section to get function code
        var paramStart = AckDataHeaderSize;
        if (data.Length < paramStart + paramLength)
            throw new InvalidOperationException("S7 response truncated in parameter section.");

        var functionCode = data[paramStart];

        if (functionCode == (byte)S7Function.CommunicationSetup)
        {
            // Setup response - no items
            return new S7Response(true, 0, 0, Array.Empty<byte[]>(), Array.Empty<byte>());
        }

        if (functionCode == (byte)S7Function.WriteVar)
        {
            // Write response - return codes in data section
            var writeDataStart = paramStart + paramLength;
            if (data.Length <= writeDataStart)
                return new S7Response(true, 0, 0, Array.Empty<byte[]>(), Array.Empty<byte>());

            var itemCount = data[paramStart + 1];
            var returnCodes = new byte[itemCount];

            for (int i = 0; i < itemCount && writeDataStart + i < data.Length; i++)
                returnCodes[i] = data[writeDataStart + i];

            var allSuccess = returnCodes.All(rc => rc == 0xFF);
            return new S7Response(allSuccess, 0, 0, Array.Empty<byte[]>(), returnCodes);
        }

        if (functionCode == (byte)S7Function.ReadVar)
        {
            // Read response - data items
            var itemCount = data[paramStart + 1];
            var dataStart = paramStart + paramLength;

            return ParseReadResponseData(data[dataStart..], itemCount);
        }

        return new S7Response(true, 0, 0, Array.Empty<byte[]>(), Array.Empty<byte>());
    }

    /// <summary>
    /// Parse the negotiated PDU size from a Communication Setup response.
    /// </summary>
    public static ushort ParseSetupPduSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < AckDataHeaderSize + 8)
            return 240;

        var paramStart = AckDataHeaderSize;
        // Params: function(1) + reserved(1) + maxAmqCalling(2) + maxAmqCalled(2) + pduSize(2)
        return BinaryPrimitives.ReadUInt16BigEndian(data[(paramStart + 6)..]);
    }

    private static S7Response ParseReadResponseData(ReadOnlySpan<byte> data, int itemCount)
    {
        var items = new byte[itemCount][];
        var returnCodes = new byte[itemCount];
        var offset = 0;

        for (int i = 0; i < itemCount; i++)
        {
            if (offset + 4 > data.Length)
                break;

            var returnCode = data[offset];
            var transportSize = data[offset + 1];
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            offset += 4;

            returnCodes[i] = returnCode;

            // Length is in bits for bit access, bytes otherwise
            int byteLength;
            if (transportSize == (byte)S7DataTransportSize.BitAccess)
                byteLength = (dataLength + 7) / 8;
            else
                byteLength = dataLength;

            if (returnCode == 0xFF && offset + byteLength <= data.Length)
            {
                items[i] = data.Slice(offset, byteLength).ToArray();
                offset += byteLength;

                // Pad to even byte boundary if necessary
                if (byteLength % 2 != 0 && offset < data.Length)
                    offset++;
            }
            else
            {
                items[i] = Array.Empty<byte>();
                if (returnCode == 0xFF)
                    offset += byteLength;
            }
        }

        var allSuccess = returnCodes.All(rc => rc == 0xFF);
        return new S7Response(allSuccess, 0, 0, items, returnCodes);
    }

    /// <summary>
    /// Write a read item specification (12 bytes each).
    /// </summary>
    private static void WriteReadItemSpec(PacketWriter writer, S7Address address)
    {
        writer.WriteUInt8(0x12); // Specification type: variable specification
        writer.WriteUInt8(0x0A); // Length of remaining: 10 bytes
        writer.WriteUInt8(0x10); // Syntax ID: S7ANY

        // Transport size
        writer.WriteUInt8((byte)address.TransportSize);

        // Length (number of items to read)
        if (address.IsString)
            writer.WriteUInt16BE((ushort)address.DataLength);
        else if (address.IsBitAddress)
            writer.WriteUInt16BE(1);
        else
            writer.WriteUInt16BE((ushort)(address.DataLength > 0 ? address.DataLength : 1));

        // DB number (0 for non-DB areas)
        writer.WriteUInt16BE((ushort)address.DbNumber);

        // Area
        writer.WriteUInt8((byte)address.Area);

        // Address (3 bytes, big-endian): byte_offset * 8 + bit_number
        var bitAddr = address.GetBitAddress();
        writer.WriteUInt8((byte)((bitAddr >> 16) & 0xFF));
        writer.WriteUInt8((byte)((bitAddr >> 8) & 0xFF));
        writer.WriteUInt8((byte)(bitAddr & 0xFF));
    }

    /// <summary>
    /// Write a data item for write requests.
    /// </summary>
    private static void WriteDataItem(PacketWriter writer, S7Address address, byte[] data)
    {
        if (address.IsBitAddress)
        {
            writer.WriteUInt8(0x00); // return code (reserved in request)
            writer.WriteUInt8((byte)S7DataTransportSize.BitAccess);
            writer.WriteUInt16BE(1); // length in bits
        }
        else
        {
            writer.WriteUInt8(0x00); // return code (reserved in request)
            writer.WriteUInt8((byte)S7DataTransportSize.ByteWordDWord);
            writer.WriteUInt16BE((ushort)(data.Length * 8)); // length in bits
        }

        writer.WriteBytes(data);

        // Pad to even byte boundary
        if (data.Length % 2 != 0)
            writer.WriteUInt8(0);
    }
}
