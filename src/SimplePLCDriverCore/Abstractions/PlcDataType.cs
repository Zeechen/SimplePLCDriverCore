namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Common PLC data types across all supported PLC platforms.
/// </summary>
public enum PlcDataType : ushort
{
    Unknown = 0,

    // Atomic types (CIP type codes for AB)
    Bool = 0x00C1,
    Sint = 0x00C2,   // signed 8-bit
    Int = 0x00C3,    // signed 16-bit
    Dint = 0x00C4,   // signed 32-bit
    Lint = 0x00C5,   // signed 64-bit
    Usint = 0x00C6,  // unsigned 8-bit
    Uint = 0x00C7,   // unsigned 16-bit
    Udint = 0x00C8,  // unsigned 32-bit
    Ulint = 0x00C9,  // unsigned 64-bit
    Real = 0x00CA,   // 32-bit float
    Lreal = 0x00CB,  // 64-bit double

    // String types
    String = 0x00DA,

    // Structure (bit 15 set indicates structure in CIP)
    Structure = 0x8000,
}
