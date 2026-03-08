using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.S7;

/// <summary>
/// COTP (Connection-Oriented Transport Protocol) per ISO 8073.
/// Sits between TPKT and S7comm. Handles connection establishment (CR/CC)
/// and data transfer (DT).
///
/// COTP PDU types:
///   0xE0 = Connection Request (CR)
///   0xD0 = Connection Confirm (CC)
///   0xF0 = Data Transfer (DT)
/// </summary>
internal static class CotpPacket
{
    public const byte PduTypeCR = 0xE0;
    public const byte PduTypeCC = 0xD0;
    public const byte PduTypeDT = 0xF0;

    // TSAP parameter codes
    private const byte ParamCallingTsap = 0xC1;
    private const byte ParamCalledTsap = 0xC2;
    private const byte ParamTpduSize = 0xC0;

    /// <summary>
    /// Build a COTP Connection Request (CR) for S7 communication.
    /// The TSAP encodes rack and slot: calling = 0x0100, called = 0x01xx where xx = rack*0x20 + slot.
    /// </summary>
    public static byte[] BuildConnectionRequest(byte rack, byte slot)
    {
        using var writer = new PacketWriter(32);

        // COTP header
        var lengthPos = writer.Length; // will patch later
        writer.WriteUInt8(0); // length indicator (placeholder)
        writer.WriteUInt8(PduTypeCR); // PDU type: Connection Request
        writer.WriteUInt16BE(0x0000); // destination reference
        writer.WriteUInt16BE(0x0001); // source reference
        writer.WriteUInt8(0x00); // class/options: class 0

        // Parameter: calling TSAP
        writer.WriteUInt8(ParamCallingTsap);
        writer.WriteUInt8(2); // parameter length
        writer.WriteUInt16BE(0x0100); // calling TSAP

        // Parameter: called TSAP
        writer.WriteUInt8(ParamCalledTsap);
        writer.WriteUInt8(2); // parameter length
        var calledTsap = (ushort)(0x0100 | (rack * 0x20 + slot));
        writer.WriteUInt16BE(calledTsap);

        // Parameter: TPDU size
        writer.WriteUInt8(ParamTpduSize);
        writer.WriteUInt8(1); // parameter length
        writer.WriteUInt8(0x0A); // 1024 bytes

        // Patch length indicator (total bytes after the length byte itself)
        var data = writer.ToArray();
        data[0] = (byte)(data.Length - 1);

        return data;
    }

    /// <summary>
    /// Validate a COTP Connection Confirm (CC) response.
    /// Returns true if the response is a valid CC.
    /// </summary>
    public static bool ValidateConnectionConfirm(ReadOnlySpan<byte> cotpData)
    {
        if (cotpData.Length < 2)
            return false;

        // byte 0 = length indicator
        // byte 1 = PDU type
        return cotpData[1] == PduTypeCC;
    }

    /// <summary>
    /// Build a COTP Data Transfer (DT) header for an S7 payload.
    /// DT header is 3 bytes: length(1) + PDU type(1) + TPDU number with EOT(1).
    /// </summary>
    public static void WriteDtHeader(PacketWriter writer)
    {
        writer.WriteUInt8(0x02); // length indicator: 2 bytes follow
        writer.WriteUInt8(PduTypeDT); // PDU type: Data Transfer
        writer.WriteUInt8(0x80); // TPDU number 0 + EOT flag (last fragment)
    }

    /// <summary>
    /// Parse a COTP DT header and return the S7 payload.
    /// </summary>
    public static ReadOnlySpan<byte> GetDtPayload(ReadOnlySpan<byte> cotpData)
    {
        if (cotpData.Length < 3)
            throw new InvalidOperationException("COTP DT frame too short.");

        var lengthIndicator = cotpData[0];

        // Validate PDU type
        if (cotpData[1] != PduTypeDT)
            throw new InvalidOperationException($"Expected COTP DT (0xF0), got 0x{cotpData[1]:X2}.");

        // Payload starts after the COTP header
        return cotpData[(lengthIndicator + 1)..];
    }

    /// <summary>
    /// Get the PDU type from a COTP frame.
    /// </summary>
    public static byte GetPduType(ReadOnlySpan<byte> cotpData)
    {
        return cotpData.Length >= 2 ? cotpData[1] : (byte)0;
    }
}
