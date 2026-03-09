namespace SimplePLCDriverCore.Protocols.Modbus;

/// <summary>
/// Byte order for multi-register Modbus data types (32-bit, 64-bit).
///
/// Modbus only defines 16-bit registers. Multi-register values (float, int32, etc.)
/// span 2+ registers and vendors use different byte/word orderings.
///
/// For a 32-bit value with bytes A, B, C, D (A = most significant):
///   ABCD - Big-endian (most common default, "Motorola" order)
///   DCBA - Little-endian ("Intel" order)
///   BADC - Big-endian byte-swapped (mid-big, some older devices)
///   CDAB - Little-endian word-swapped (mid-little, common alternative)
/// </summary>
public enum ModbusByteOrder
{
    /// <summary>Big-endian. Bytes: A B C D. Most common default.</summary>
    ABCD = 0,

    /// <summary>Little-endian. Bytes: D C B A.</summary>
    DCBA = 1,

    /// <summary>Big-endian byte-swapped. Bytes: B A D C.</summary>
    BADC = 2,

    /// <summary>Little-endian word-swapped. Bytes: C D A B.</summary>
    CDAB = 3,
}

/// <summary>
/// Byte order reordering helpers for multi-register Modbus values.
/// </summary>
internal static class ModbusByteOrderHelper
{
    /// <summary>
    /// Reorder 4 bytes (2 registers) from wire format to native big-endian (ABCD)
    /// based on the specified byte order of the source device.
    /// </summary>
    public static void Reorder4(Span<byte> bytes, ModbusByteOrder order)
    {
        if (bytes.Length < 4) return;

        switch (order)
        {
            case ModbusByteOrder.ABCD:
                // Already in big-endian order, no change needed
                break;

            case ModbusByteOrder.DCBA:
                // Full reversal: D C B A -> A B C D
                (bytes[0], bytes[1], bytes[2], bytes[3]) =
                    (bytes[3], bytes[2], bytes[1], bytes[0]);
                break;

            case ModbusByteOrder.BADC:
                // Byte swap within each word: B A D C -> A B C D
                (bytes[0], bytes[1]) = (bytes[1], bytes[0]);
                (bytes[2], bytes[3]) = (bytes[3], bytes[2]);
                break;

            case ModbusByteOrder.CDAB:
                // Word swap: C D A B -> A B C D
                (bytes[0], bytes[1], bytes[2], bytes[3]) =
                    (bytes[2], bytes[3], bytes[0], bytes[1]);
                break;
        }
    }

    /// <summary>
    /// Reorder 8 bytes (4 registers) from wire format to native big-endian
    /// based on the specified byte order. Applies the same logic per 32-bit word pair.
    /// </summary>
    public static void Reorder8(Span<byte> bytes, ModbusByteOrder order)
    {
        if (bytes.Length < 8) return;

        switch (order)
        {
            case ModbusByteOrder.ABCD:
                break;

            case ModbusByteOrder.DCBA:
                (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7]) =
                    (bytes[7], bytes[6], bytes[5], bytes[4], bytes[3], bytes[2], bytes[1], bytes[0]);
                break;

            case ModbusByteOrder.BADC:
                (bytes[0], bytes[1]) = (bytes[1], bytes[0]);
                (bytes[2], bytes[3]) = (bytes[3], bytes[2]);
                (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
                (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
                break;

            case ModbusByteOrder.CDAB:
                (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7]) =
                    (bytes[2], bytes[3], bytes[0], bytes[1], bytes[6], bytes[7], bytes[4], bytes[5]);
                break;
        }
    }

    /// <summary>
    /// Reorder 4 bytes from native big-endian (ABCD) to the target device byte order for writing.
    /// </summary>
    public static void ToWire4(Span<byte> bytes, ModbusByteOrder order)
    {
        // The inverse of Reorder4: convert FROM ABCD TO the target order
        if (bytes.Length < 4) return;

        switch (order)
        {
            case ModbusByteOrder.ABCD:
                break;

            case ModbusByteOrder.DCBA:
                (bytes[0], bytes[1], bytes[2], bytes[3]) =
                    (bytes[3], bytes[2], bytes[1], bytes[0]);
                break;

            case ModbusByteOrder.BADC:
                (bytes[0], bytes[1]) = (bytes[1], bytes[0]);
                (bytes[2], bytes[3]) = (bytes[3], bytes[2]);
                break;

            case ModbusByteOrder.CDAB:
                (bytes[0], bytes[1], bytes[2], bytes[3]) =
                    (bytes[2], bytes[3], bytes[0], bytes[1]);
                break;
        }
    }

    /// <summary>
    /// Reorder 8 bytes from native big-endian to the target device byte order for writing.
    /// </summary>
    public static void ToWire8(Span<byte> bytes, ModbusByteOrder order)
    {
        if (bytes.Length < 8) return;

        switch (order)
        {
            case ModbusByteOrder.ABCD:
                break;

            case ModbusByteOrder.DCBA:
                (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7]) =
                    (bytes[7], bytes[6], bytes[5], bytes[4], bytes[3], bytes[2], bytes[1], bytes[0]);
                break;

            case ModbusByteOrder.BADC:
                (bytes[0], bytes[1]) = (bytes[1], bytes[0]);
                (bytes[2], bytes[3]) = (bytes[3], bytes[2]);
                (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
                (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
                break;

            case ModbusByteOrder.CDAB:
                (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7]) =
                    (bytes[2], bytes[3], bytes[0], bytes[1], bytes[6], bytes[7], bytes[4], bytes[5]);
                break;
        }
    }
}
