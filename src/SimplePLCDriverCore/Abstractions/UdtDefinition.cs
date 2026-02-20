namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Definition of a User Defined Type (UDT) / structure read from the PLC.
/// </summary>
public sealed class UdtDefinition
{
    /// <summary>UDT type name (e.g., "MyUDT").</summary>
    public required string Name { get; init; }

    /// <summary>Total byte size of the structure in memory.</summary>
    public int ByteSize { get; init; }

    /// <summary>CIP template instance ID.</summary>
    public ushort TemplateInstanceId { get; init; }

    /// <summary>Structure members in order.</summary>
    public required IReadOnlyList<UdtMember> Members { get; init; }

    public override string ToString() =>
        $"{Name} ({ByteSize} bytes, {Members.Count} members)";
}

/// <summary>
/// A single member within a UDT definition.
/// </summary>
public sealed class UdtMember
{
    /// <summary>Member name.</summary>
    public required string Name { get; init; }

    /// <summary>Data type of this member.</summary>
    public PlcDataType DataType { get; init; }

    /// <summary>Human-readable type name.</summary>
    public required string TypeName { get; init; }

    /// <summary>Byte offset within the structure.</summary>
    public int Offset { get; init; }

    /// <summary>Size of this member in bytes.</summary>
    public int Size { get; init; }

    /// <summary>Array dimensions if this member is an array.</summary>
    public int[] Dimensions { get; init; } = [];

    /// <summary>Bit number for BOOL members packed in a DINT (0-31), -1 if not a bit member.</summary>
    public int BitOffset { get; init; } = -1;

    /// <summary>True if this member is itself a structure.</summary>
    public bool IsStructure { get; init; }

    /// <summary>Template instance ID if this member is a nested structure.</summary>
    public ushort TemplateInstanceId { get; init; }

    public override string ToString() =>
        $"{Name}: {TypeName} @ offset {Offset}";
}
