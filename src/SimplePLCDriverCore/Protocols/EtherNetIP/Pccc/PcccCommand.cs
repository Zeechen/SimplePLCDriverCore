using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

/// <summary>
/// Builds PCCC command frames for SLC/MicroLogix/PLC-5 communication.
///
/// PCCC (Programmable Controller Communication Commands) operates over CIP
/// using the PCCC Object (Class 0x67). The flow is:
///
///   1. EtherNet/IP session (RegisterSession)
///   2. CIP Unconnected Send wrapping a "Execute PCCC" service (0x4B)
///        to the PCCC Object (Class 0x67, Instance 1)
///   3. PCCC command frame inside the CIP data
///
/// PCCC command frame format:
///   [PCCC header bytes] + [Command] + [Status] + [Transaction ID] + [Function] + [Data]
///
/// For Execute PCCC (CIP service 0x4B):
///   CIP Service: 0x4B
///   CIP Path: Class 0x67, Instance 1
///   CIP Data: requestor_id_length + requestor_id + PCCC_command_data
///
/// The requestor ID is typically 7 bytes for SLC:
///   length(1) + CIP_vendor_id(2) + CIP_serial(4)
/// </summary>
internal static class PcccCommand
{
    /// <summary>CIP service code for Execute PCCC.</summary>
    private const byte ExecutePcccService = 0x4B;

    /// <summary>Default vendor ID for PCCC requestor identification.</summary>
    private const ushort DefaultVendorId = 0x0001;

    /// <summary>
    /// Build a CIP Execute PCCC request that wraps a PCCC read command.
    ///
    /// The complete CIP message:
    ///   Service (0x4B)
    ///   Path Size (words)
    ///   Path: Class 0x67, Instance 1
    ///   Requestor ID Length (1 byte)
    ///   Requestor ID: vendor_id (2 LE) + serial_number (4 LE)
    ///   PCCC Command (0x0F)
    ///   PCCC Status (0x00)
    ///   PCCC Transaction ID (2 bytes LE)
    ///   PCCC Function Code
    ///   Byte Size (1 byte) - number of bytes to read
    ///   File Number (1 byte)
    ///   File Type (1 byte)
    ///   Element Number (1 byte)
    ///   Sub-Element Number (1 byte)
    /// </summary>
    public static byte[] BuildReadRequest(
        PcccAddress address,
        ushort transactionId,
        uint originatorSerial,
        ushort vendorId = DefaultVendorId)
    {
        var readSize = (byte)address.GetReadSize();

        using var writer = new PacketWriter(64);

        // CIP header: service + path
        WriteCipHeader(writer);

        // Requestor ID
        WriteRequestorId(writer, vendorId, originatorSerial);

        // PCCC Command frame
        writer.WriteUInt8(PcccTypes.TypedCommand);  // Command: 0x0F
        writer.WriteUInt8(0x00);                      // Status: 0x00
        writer.WriteUInt16LE(transactionId);           // Transaction ID

        // Function code: Protected Typed Logical Read with 3 Address Fields
        writer.WriteUInt8(PcccTypes.FnProtectedTypedLogicalRead3);

        // Read size (bytes to read)
        writer.WriteUInt8(readSize);

        // Address fields: file number, file type, element, sub-element
        writer.WriteUInt8((byte)address.FileNumber);
        writer.WriteUInt8((byte)address.PcccFileType);
        writer.WriteUInt8((byte)address.Element);
        writer.WriteUInt8((byte)Math.Max(address.SubElement, 0));

        return writer.ToArray();
    }

    /// <summary>
    /// Build a CIP Execute PCCC request that wraps a PCCC write command.
    ///
    /// Similar to read, but function code is Protected Typed Logical Write
    /// and the data bytes follow the address fields.
    /// </summary>
    public static byte[] BuildWriteRequest(
        PcccAddress address,
        ReadOnlySpan<byte> data,
        ushort transactionId,
        uint originatorSerial,
        ushort vendorId = DefaultVendorId)
    {
        using var writer = new PacketWriter(64 + data.Length);

        // CIP header: service + path
        WriteCipHeader(writer);

        // Requestor ID
        WriteRequestorId(writer, vendorId, originatorSerial);

        // PCCC Command frame
        writer.WriteUInt8(PcccTypes.TypedCommand);  // Command: 0x0F
        writer.WriteUInt8(0x00);                      // Status: 0x00
        writer.WriteUInt16LE(transactionId);           // Transaction ID

        // Function code: Protected Typed Logical Write with 3 Address Fields
        writer.WriteUInt8(PcccTypes.FnProtectedTypedLogicalWrite3);

        // Write size (bytes to write)
        writer.WriteUInt8((byte)data.Length);

        // Address fields
        writer.WriteUInt8((byte)address.FileNumber);
        writer.WriteUInt8((byte)address.PcccFileType);
        writer.WriteUInt8((byte)address.Element);
        writer.WriteUInt8((byte)Math.Max(address.SubElement, 0));

        // Data to write
        writer.WriteBytes(data);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse a PCCC response from the CIP Execute PCCC reply data.
    ///
    /// CIP Execute PCCC response data format:
    ///   Requestor ID Length (1 byte)
    ///   Requestor ID (variable)
    ///   PCCC Command | Reply (1 byte) - command code OR'd with 0x40
    ///   PCCC Status (1 byte) - 0x00 = success
    ///   Transaction ID (2 bytes LE)
    ///   [Extended Status (optional, if status has ext bit set)]
    ///   Response Data (variable)
    /// </summary>
    public static PcccResponse ParseResponse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("PCCC response too short");

        var reader = new PacketReader(data);

        // Skip requestor ID
        var requestorIdLength = reader.ReadUInt8();
        reader.Skip(requestorIdLength);

        // PCCC reply header
        var command = reader.ReadUInt8();
        var status = reader.ReadUInt8();
        var transactionId = reader.ReadUInt16LE();

        // Check for extended status (lower nibble of status indicates ext status size)
        var extStatusSize = status & 0x0F;
        if (extStatusSize > 0)
        {
            // Extended status is extStatusSize * 2 bytes
            reader.Skip(extStatusSize * 2);
        }

        // Remaining data is the response payload
        var responseData = data.Slice(reader.Position);

        return new PcccResponse(command, status, transactionId, responseData);
    }

    /// <summary>
    /// Build an Unconnected Send wrapper for a PCCC CIP request, routing to the PLC processor.
    /// This wraps the Execute PCCC message in CIP's Unconnected Send service.
    /// </summary>
    public static byte[] WrapInUnconnectedSend(byte[] pcccCipRequest, byte slot)
    {
        return CipMessage.BuildUnconnectedSend(pcccCipRequest, slot);
    }

    // --- Private Helpers ---

    /// <summary>
    /// Write the CIP service header for Execute PCCC.
    /// Service: 0x4B, Path: Class 0x67, Instance 1
    /// </summary>
    private static void WriteCipHeader(PacketWriter writer)
    {
        writer.WriteUInt8(ExecutePcccService);

        // Path: Class 0x67 (PCCC Object), Instance 1
        var path = CipPath.BuildClassInstancePath(CipClasses.PcccObject, 1);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
    }

    /// <summary>
    /// Write the PCCC requestor ID block.
    /// Format: length (1 byte) + vendor_id (2 LE) + serial_number (4 LE)
    /// Total: 7 bytes (length=6, data=6 bytes)
    /// </summary>
    private static void WriteRequestorId(
        PacketWriter writer, ushort vendorId, uint serialNumber)
    {
        writer.WriteUInt8(7);               // Requestor ID length (includes length byte itself in some implementations)
                                            // Actually: the length byte value = number of bytes that follow
        // Correction: length = 6 (vendor_id 2 + serial 4)
        // But some implementations include CIP version bytes. Let's use the common 7-byte format.
        // Format: length(1) = 7, then: CIP_vendor_id(2) + CIP_serial(4) = 6 bytes + ...
        // Actually the standard format is:
        //   requestor_id_length = number of bytes in requestor_id (not including the length byte itself)
        //   For SLC: typically 6 bytes = vendor_id(2) + serial(4)

        // Rewrite: length byte = 6 (just vendor + serial)
        writer.Position -= 1; // back up
        writer.WriteUInt8(6);               // Requestor ID data length
        writer.WriteUInt16LE(vendorId);     // Vendor ID
        writer.WriteUInt32LE(serialNumber); // Originator serial number
    }
}

/// <summary>
/// Parsed PCCC response from an Execute PCCC CIP reply.
/// </summary>
internal readonly struct PcccResponse
{
    /// <summary>PCCC command code from the reply (with reply bit set).</summary>
    public byte Command { get; }

    /// <summary>PCCC status code. 0x00 = success.</summary>
    public byte Status { get; }

    /// <summary>Transaction ID echoed from the request.</summary>
    public ushort TransactionId { get; }

    /// <summary>Response payload data (after status fields).</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Whether the PCCC response indicates success.</summary>
    public bool IsSuccess => (Status & 0xF0) == 0x00;

    /// <summary>Get a user-friendly error message.</summary>
    public string GetErrorMessage() =>
        IsSuccess ? "Success" : PcccTypes.GetStatusMessage(Status);

    public PcccResponse(byte command, byte status, ushort transactionId, ReadOnlyMemory<byte> data)
    {
        Command = command;
        Status = status;
        TransactionId = transactionId;
        Data = data;
    }
}
