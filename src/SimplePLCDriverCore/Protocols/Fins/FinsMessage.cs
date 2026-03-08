using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.Fins;

/// <summary>
/// FINS command codes.
/// </summary>
internal static class FinsCommands
{
    // Memory Area Read/Write
    public const ushort MemoryAreaRead = 0x0101;
    public const ushort MemoryAreaWrite = 0x0102;
}

/// <summary>
/// Parsed FINS response.
/// </summary>
internal readonly struct FinsResponse
{
    public bool IsSuccess { get; }
    public byte MainResponseCode { get; }
    public byte SubResponseCode { get; }
    public ReadOnlyMemory<byte> Data { get; }

    public FinsResponse(bool isSuccess, byte mainCode, byte subCode, ReadOnlyMemory<byte> data)
    {
        IsSuccess = isSuccess;
        MainResponseCode = mainCode;
        SubResponseCode = subCode;
        Data = data;
    }

    public string GetErrorMessage()
    {
        if (IsSuccess) return string.Empty;

        return MainResponseCode switch
        {
            0x00 when SubResponseCode != 0x00 =>
                $"FINS warning: main=0x{MainResponseCode:X2}, sub=0x{SubResponseCode:X2}",
            0x01 => $"Local node error (sub=0x{SubResponseCode:X2})",
            0x02 => $"Destination node error (sub=0x{SubResponseCode:X2})",
            0x03 => $"Controller error (sub=0x{SubResponseCode:X2})",
            0x04 => $"Service unsupported (sub=0x{SubResponseCode:X2})",
            0x05 => $"Routing error (sub=0x{SubResponseCode:X2})",
            0x10 => $"Command format error (sub=0x{SubResponseCode:X2})",
            0x11 => $"Parameter error (sub=0x{SubResponseCode:X2})",
            0x20 => $"Read not possible (sub=0x{SubResponseCode:X2})",
            0x21 => $"Write not possible (sub=0x{SubResponseCode:X2})",
            0x22 => $"Not executable in current mode (sub=0x{SubResponseCode:X2})",
            0x23 => $"No unit (sub=0x{SubResponseCode:X2})",
            0x25 => $"File/memory error (sub=0x{SubResponseCode:X2})",
            _ => $"FINS error: main=0x{MainResponseCode:X2}, sub=0x{SubResponseCode:X2}"
        };
    }
}

/// <summary>
/// FINS message builder and parser.
///
/// FINS/TCP frame structure:
///   TCP header (16 bytes):
///     byte 0-3: "FINS" magic (0x46494E53)
///     byte 4-7: Length (remaining bytes after this field)
///     byte 8-11: Command (0x00000002 = FINS frame)
///     byte 12-15: Error code (0x00000000 = OK)
///
///   FINS header (10 bytes):
///     byte 0: ICF (Information Control Field)
///     byte 1: RSV (Reserved, always 0)
///     byte 2: GCT (Gateway Count, always 0x02)
///     byte 3: DNA (Destination Network Address)
///     byte 4: DA1 (Destination Node Address)
///     byte 5: DA2 (Destination Unit Address)
///     byte 6: SNA (Source Network Address)
///     byte 7: SA1 (Source Node Address)
///     byte 8: SA2 (Source Unit Address)
///     byte 9: SID (Service ID)
///
///   FINS command (2 bytes): command code
///   FINS data: variable
/// </summary>
internal static class FinsMessage
{
    // FINS/TCP magic
    public static readonly byte[] FinsMagic = [0x46, 0x49, 0x4E, 0x53]; // "FINS"
    public const int TcpHeaderSize = 16;
    public const int FinsHeaderSize = 10;
    public const int CommandSize = 2;

    // FINS/TCP commands
    public const uint TcpCommandSendFrame = 0x00000002;
    public const uint TcpCommandNodeAddressRequest = 0x00000000;
    public const uint TcpCommandNodeAddressResponse = 0x00000001;

    /// <summary>
    /// Build a FINS/TCP node address request (initial handshake).
    /// The PLC responds with the client node address to use.
    /// </summary>
    public static byte[] BuildNodeAddressRequest(byte clientNode = 0)
    {
        using var writer = new PacketWriter(24);

        // TCP header
        writer.WriteBytes(FinsMagic);
        writer.WriteUInt32BE(8); // length: 8 bytes remaining
        writer.WriteUInt32BE(TcpCommandNodeAddressRequest);
        writer.WriteUInt32BE(0); // error code

        // Node address data
        writer.WriteUInt32BE(0); // client node (0 = auto-assign)

        return writer.ToArray();
    }

    /// <summary>
    /// Parse a FINS/TCP node address response.
    /// Returns (clientNode, serverNode).
    /// </summary>
    public static (byte ClientNode, byte ServerNode) ParseNodeAddressResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < TcpHeaderSize + 8)
            throw new InvalidOperationException("FINS node address response too short.");

        var errorCode = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        if (errorCode != 0)
            throw new IOException($"FINS node address error: 0x{errorCode:X8}");

        var clientNode = (byte)BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var serverNode = (byte)BinaryPrimitives.ReadUInt32BigEndian(data[20..]);

        return (clientNode, serverNode);
    }

    /// <summary>
    /// Build a FINS Memory Area Read request.
    /// </summary>
    public static byte[] BuildReadRequest(
        FinsAddress address, byte sid,
        byte sourceNode, byte destNode, ushort wordCount = 1)
    {
        using var writer = new PacketWriter(64);

        // FINS command payload
        using var finsWriter = new PacketWriter(32);

        // Command: Memory Area Read
        finsWriter.WriteUInt16BE(FinsCommands.MemoryAreaRead);

        // Area code
        finsWriter.WriteUInt8((byte)address.Area);

        // Address: word (2 bytes BE) + bit (1 byte)
        finsWriter.WriteUInt16BE((ushort)address.Address);
        finsWriter.WriteUInt8((byte)(address.IsBitAddress ? address.BitNumber : 0));

        // Number of items to read
        finsWriter.WriteUInt16BE(address.IsBitAddress ? (ushort)1 : wordCount);

        var finsPayload = finsWriter.GetWrittenSpan();

        // Build complete FINS/TCP frame
        WriteTcpFinsFrame(writer, finsPayload, sid, sourceNode, destNode);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a FINS Memory Area Write request.
    /// </summary>
    public static byte[] BuildWriteRequest(
        FinsAddress address, byte[] data, byte sid,
        byte sourceNode, byte destNode, ushort wordCount = 1)
    {
        using var writer = new PacketWriter(64 + data.Length);

        // FINS command payload
        using var finsWriter = new PacketWriter(32 + data.Length);

        // Command: Memory Area Write
        finsWriter.WriteUInt16BE(FinsCommands.MemoryAreaWrite);

        // Area code
        finsWriter.WriteUInt8((byte)address.Area);

        // Address
        finsWriter.WriteUInt16BE((ushort)address.Address);
        finsWriter.WriteUInt8((byte)(address.IsBitAddress ? address.BitNumber : 0));

        // Number of items
        finsWriter.WriteUInt16BE(address.IsBitAddress ? (ushort)1 : wordCount);

        // Data
        finsWriter.WriteBytes(data);

        var finsPayload = finsWriter.GetWrittenSpan();

        // Build complete FINS/TCP frame
        WriteTcpFinsFrame(writer, finsPayload, sid, sourceNode, destNode);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse a FINS/TCP response frame. Extracts the FINS response data.
    /// </summary>
    public static FinsResponse ParseResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < TcpHeaderSize)
            throw new InvalidOperationException("FINS/TCP response too short.");

        // Check TCP header
        var tcpErrorCode = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        if (tcpErrorCode != 0)
            throw new IOException($"FINS/TCP error: 0x{tcpErrorCode:X8}");

        var tcpCommand = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        if (tcpCommand != TcpCommandSendFrame)
            throw new InvalidOperationException($"Unexpected FINS/TCP command: 0x{tcpCommand:X8}");

        // Skip TCP header -> FINS header
        var finsData = data[TcpHeaderSize..];
        if (finsData.Length < FinsHeaderSize + CommandSize + 2)
            throw new InvalidOperationException("FINS response data too short.");

        // Skip FINS header (10 bytes) + command echo (2 bytes) -> response codes
        var responseStart = FinsHeaderSize + CommandSize;
        var mainCode = finsData[responseStart];
        var subCode = finsData[responseStart + 1];

        var isSuccess = mainCode == 0x00 && subCode == 0x00;
        var responseData = finsData.Length > responseStart + 2
            ? finsData[(responseStart + 2)..].ToArray()
            : Array.Empty<byte>();

        return new FinsResponse(isSuccess, mainCode, subCode, responseData);
    }

    /// <summary>
    /// Get the total frame length from the FINS/TCP header.
    /// Used with ReceiveFramedAsync.
    /// </summary>
    public static int GetLengthFromHeader(byte[] header)
    {
        if (header.Length < 8)
            throw new InvalidOperationException("FINS/TCP header too short.");

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        return (int)(8 + payloadLength); // magic(4) + length(4) + payload
    }

    private static void WriteTcpFinsFrame(
        PacketWriter writer, ReadOnlySpan<byte> finsPayload,
        byte sid, byte sourceNode, byte destNode)
    {
        // Build FINS header + payload
        using var finsFrameWriter = new PacketWriter(FinsHeaderSize + finsPayload.Length);

        // FINS header
        finsFrameWriter.WriteUInt8(0x80); // ICF: command (bit 7 = 1), no response required (bit 6 = 0 for request)
        finsFrameWriter.WriteUInt8(0x00); // RSV
        finsFrameWriter.WriteUInt8(0x02); // GCT: gateway count
        finsFrameWriter.WriteUInt8(0x00); // DNA: destination network (local)
        finsFrameWriter.WriteUInt8(destNode); // DA1: destination node
        finsFrameWriter.WriteUInt8(0x00); // DA2: destination unit
        finsFrameWriter.WriteUInt8(0x00); // SNA: source network (local)
        finsFrameWriter.WriteUInt8(sourceNode); // SA1: source node
        finsFrameWriter.WriteUInt8(0x00); // SA2: source unit
        finsFrameWriter.WriteUInt8(sid); // SID: service ID

        finsFrameWriter.WriteBytes(finsPayload);

        var finsFrame = finsFrameWriter.GetWrittenSpan();

        // TCP header
        writer.WriteBytes(FinsMagic);
        writer.WriteUInt32BE((uint)(8 + finsFrame.Length)); // length after this field
        writer.WriteUInt32BE(TcpCommandSendFrame);
        writer.WriteUInt32BE(0); // error code

        writer.WriteBytes(finsFrame);
    }
}
