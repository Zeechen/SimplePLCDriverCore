using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// CIP path encoding utilities.
///
/// CIP uses EPATH segments to address objects and tags:
///   - Logical segments (0x20=class, 0x24=instance, 0x28=member)
///   - Symbolic segments (0x91) for tag names
///   - Port segments for routing (backplane/slot)
///   - Data segments for element (array index) access
/// </summary>
internal static class CipPath
{
    // Logical segment types
    private const byte LogicalSegmentClass8Bit = 0x20;
    private const byte LogicalSegmentClass16Bit = 0x21;
    private const byte LogicalSegmentInstance8Bit = 0x24;
    private const byte LogicalSegmentInstance16Bit = 0x25;
    private const byte LogicalSegmentMember8Bit = 0x28;
    private const byte LogicalSegmentMember16Bit = 0x29;

    // Symbolic segment
    private const byte SymbolicSegment = 0x91;

    // Port segment
    private const byte PortSegmentBackplane = 0x01;

    /// <summary>
    /// Encode a symbolic tag name into CIP EPATH segments.
    /// Handles dotted paths (MyUDT.Member), array indices (MyArray[5]),
    /// and bit access (MyDINT.5).
    ///
    /// Examples:
    ///   "MyTag"          -> [0x91, 5, 'M','y','T','a','g', pad]
    ///   "MyArray[3]"     -> [0x91, 7, 'M','y','A','r','r','a','y', pad, 0x28, 0x03]
    ///   "MyUDT.Member"   -> [0x91, 5, 'M','y','U','D','T', pad, 0x91, 6, 'M','e','m','b','e','r']
    /// </summary>
    public static byte[] EncodeSymbolicPath(string tagName)
    {
        using var writer = new PacketWriter(tagName.Length * 2 + 16);
        EncodeSymbolicPath(writer, tagName);
        return writer.ToArray();
    }

    /// <summary>Encode a symbolic tag path directly into a PacketWriter.</summary>
    public static void EncodeSymbolicPath(PacketWriter writer, string tagName)
    {
        // Split the tag name on '.' for UDT member access
        // But handle array indices within each segment
        var parts = tagName.Split('.');

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];

            // Check for array index: TagName[index]
            var bracketPos = part.IndexOf('[');
            if (bracketPos >= 0)
            {
                // Encode the tag name portion
                var name = part[..bracketPos];
                EncodeSymbolicSegment(writer, name);

                // Parse and encode array indices (may be multi-dimensional: [1,2,3])
                var indexStr = part[(bracketPos + 1)..part.IndexOf(']')];
                var indices = indexStr.Split(',');
                foreach (var idx in indices)
                {
                    var index = uint.Parse(idx.Trim());
                    EncodeElementSegment(writer, index);
                }
            }
            else if (i > 0 && uint.TryParse(part, out var bitIndex))
            {
                // Bit access: MyDINT.5 - the "5" after dot is treated as a bit offset
                // This is encoded as a member segment
                EncodeMemberSegment(writer, bitIndex);
            }
            else
            {
                // Regular symbolic name
                EncodeSymbolicSegment(writer, part);
            }
        }
    }

    /// <summary>
    /// Encode a single ANSI Extended Symbol Segment (0x91).
    /// Format: 0x91, length, name bytes, [pad byte if odd length]
    /// </summary>
    public static void EncodeSymbolicSegment(PacketWriter writer, string name)
    {
        writer.WriteUInt8(SymbolicSegment);
        writer.WriteUInt8((byte)name.Length);
        writer.WriteAsciiPadded(name);
    }

    /// <summary>
    /// Encode an element segment (array index).
    /// Uses 8-bit (0x28), 16-bit (0x29), or 32-bit (0x2A) encoding based on value.
    /// </summary>
    public static void EncodeElementSegment(PacketWriter writer, uint index)
    {
        if (index <= 0xFF)
        {
            writer.WriteUInt8(0x28);
            writer.WriteUInt8((byte)index);
        }
        else if (index <= 0xFFFF)
        {
            writer.WriteUInt8(0x29);
            writer.WriteUInt8(0x00); // pad
            writer.WriteUInt16LE((ushort)index);
        }
        else
        {
            writer.WriteUInt8(0x2A);
            writer.WriteUInt8(0x00); // pad
            writer.WriteUInt32LE(index);
        }
    }

    /// <summary>
    /// Encode a member segment (bit access in DINT, or explicit member ID).
    /// </summary>
    public static void EncodeMemberSegment(PacketWriter writer, uint memberId)
    {
        if (memberId <= 0xFF)
        {
            writer.WriteUInt8(LogicalSegmentMember8Bit);
            writer.WriteUInt8((byte)memberId);
        }
        else
        {
            writer.WriteUInt8(LogicalSegmentMember16Bit);
            writer.WriteUInt8(0x00); // pad
            writer.WriteUInt16LE((ushort)memberId);
        }
    }

    /// <summary>
    /// Encode a class segment (e.g., for Symbol Object 0x6B, Template Object 0x6C).
    /// </summary>
    public static void EncodeClassSegment(PacketWriter writer, ushort classId)
    {
        if (classId <= 0xFF)
        {
            writer.WriteUInt8(LogicalSegmentClass8Bit);
            writer.WriteUInt8((byte)classId);
        }
        else
        {
            writer.WriteUInt8(LogicalSegmentClass16Bit);
            writer.WriteUInt8(0x00); // pad
            writer.WriteUInt16LE(classId);
        }
    }

    /// <summary>
    /// Encode an instance segment.
    /// </summary>
    public static void EncodeInstanceSegment(PacketWriter writer, uint instanceId)
    {
        if (instanceId <= 0xFF)
        {
            writer.WriteUInt8(LogicalSegmentInstance8Bit);
            writer.WriteUInt8((byte)instanceId);
        }
        else
        {
            writer.WriteUInt8(LogicalSegmentInstance16Bit);
            writer.WriteUInt8(0x00); // pad
            writer.WriteUInt16LE((ushort)instanceId);
        }
    }

    /// <summary>
    /// Build a CIP path to a specific class + instance (e.g., for Symbol Object).
    /// </summary>
    public static byte[] BuildClassInstancePath(ushort classId, uint instanceId)
    {
        using var writer = new PacketWriter(8);
        EncodeClassSegment(writer, classId);
        EncodeInstanceSegment(writer, instanceId);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a CIP path to just a class (instance 0 / class-level request).
    /// </summary>
    public static byte[] BuildClassPath(ushort classId)
    {
        using var writer = new PacketWriter(4);
        EncodeClassSegment(writer, classId);
        return writer.ToArray();
    }

    /// <summary>
    /// Encode a route path for addressing the PLC processor.
    /// For ControlLogix: port 1 (backplane), slot N
    /// For CompactLogix: typically port 1, slot 0
    /// </summary>
    public static byte[] EncodeRoutePath(byte slot)
    {
        return [PortSegmentBackplane, slot];
    }

    /// <summary>
    /// Calculate the path size in 16-bit words (as required by CIP message headers).
    /// </summary>
    public static byte GetPathSizeInWords(int pathByteLength) =>
        (byte)((pathByteLength + 1) / 2);
}
