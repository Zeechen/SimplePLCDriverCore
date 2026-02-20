namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Metadata descriptor for a PLC tag discovered via tag browsing.
/// </summary>
public sealed class PlcTagInfo
{
    /// <summary>Tag name (e.g., "MyDINT", "Program:MainProgram.LocalTag").</summary>
    public required string Name { get; init; }

    /// <summary>PLC data type.</summary>
    public PlcDataType DataType { get; init; }

    /// <summary>Human-readable type name (e.g., "DINT", "REAL", "MyCustomUDT").</summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Array dimensions. Empty for scalars.
    /// [10] for 1D array, [10, 5] for 2D, [10, 5, 3] for 3D.
    /// </summary>
    public int[] Dimensions { get; init; } = [];

    /// <summary>True if this tag is a UDT/structure type.</summary>
    public bool IsStructure { get; init; }

    /// <summary>True if this tag is scoped to a program.</summary>
    public bool IsProgramScoped { get; init; }

    /// <summary>Program name if program-scoped, null otherwise.</summary>
    public string? ProgramName { get; init; }

    /// <summary>CIP symbol instance ID (for Allen-Bradley).</summary>
    public uint InstanceId { get; init; }

    /// <summary>Raw CIP type code (for internal use by drivers).</summary>
    public ushort RawTypeCode { get; init; }

    /// <summary>Template instance ID for structure types (lower bits of type code when bit 15 is set).</summary>
    public ushort TemplateInstanceId { get; init; }

    public override string ToString()
    {
        var dims = Dimensions.Length > 0
            ? $"[{string.Join(",", Dimensions)}]"
            : string.Empty;
        var scope = IsProgramScoped ? $" (Program:{ProgramName})" : string.Empty;
        return $"{Name}: {TypeName}{dims}{scope}";
    }
}
