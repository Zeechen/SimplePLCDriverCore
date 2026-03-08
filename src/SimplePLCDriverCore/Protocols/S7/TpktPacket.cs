using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.S7;

/// <summary>
/// TPKT (Transport Protocol Data Unit) framing per RFC 1006.
/// Used as the outermost transport layer for S7comm over TCP port 102.
///
/// Frame format (4 bytes):
///   byte 0: Version (always 3)
///   byte 1: Reserved (always 0)
///   byte 2-3: Length (big-endian, includes header)
/// </summary>
internal static class TpktPacket
{
    public const int HeaderSize = 4;
    public const byte Version = 3;

    /// <summary>
    /// Wrap payload in a TPKT frame.
    /// </summary>
    public static void Write(PacketWriter writer, ReadOnlySpan<byte> payload)
    {
        var totalLength = (ushort)(HeaderSize + payload.Length);
        writer.WriteUInt8(Version);
        writer.WriteUInt8(0); // reserved
        writer.WriteUInt16BE(totalLength);
        writer.WriteBytes(payload);
    }

    /// <summary>
    /// Extract the total message length from a TPKT header.
    /// Used with ReceiveFramedAsync.
    /// </summary>
    public static int GetLengthFromHeader(byte[] header)
    {
        if (header.Length < HeaderSize)
            throw new InvalidOperationException("TPKT header too short.");

        return BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
    }

    /// <summary>
    /// Parse a TPKT frame and return the payload (everything after the 4-byte header).
    /// </summary>
    public static ReadOnlySpan<byte> GetPayload(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderSize)
            throw new InvalidOperationException("TPKT frame too short.");

        return frame[HeaderSize..];
    }
}
