using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// Factory for creating PLC driver instances.
/// </summary>
public static class PlcDriverFactory
{
    /// <summary>
    /// Create a driver for Allen-Bradley ControlLogix or CompactLogix PLCs.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number. Default 0 (CompactLogix). ControlLogix may vary.</param>
    /// <param name="options">Optional connection options.</param>
    /// <returns>A LogixDriver implementing IPlcDriver and ITagBrowser.</returns>
    public static LogixDriver CreateLogix(
        string host, byte slot = 0, ConnectionOptions? options = null)
    {
        return new LogixDriver(host, slot, options);
    }

    /// <summary>
    /// Create a driver for Allen-Bradley CompactLogix (always slot 0).
    /// </summary>
    public static LogixDriver CreateCompactLogix(
        string host, ConnectionOptions? options = null)
    {
        return new LogixDriver(host, slot: 0, options);
    }

    /// <summary>
    /// Create a driver for Allen-Bradley ControlLogix with a specific slot.
    /// </summary>
    public static LogixDriver CreateControlLogix(
        string host, byte slot, ConnectionOptions? options = null)
    {
        return new LogixDriver(host, slot, options);
    }

    // --- SLC / MicroLogix / PLC-5 ---

    /// <summary>
    /// Create a driver for Allen-Bradley SLC 500 PLCs.
    /// Uses PCCC over CIP with file-based addressing (N7:0, F8:1, B3:0/5, etc.).
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number. Default 0.</param>
    /// <param name="options">Optional connection options.</param>
    public static SlcDriver CreateSlc(
        string host, byte slot = 0, ConnectionOptions? options = null)
    {
        return new SlcDriver(host, slot, SlcPlcType.Slc500, options);
    }

    /// <summary>
    /// Create a driver for Allen-Bradley MicroLogix PLCs (1000, 1100, 1200, 1400, 1500).
    /// Uses PCCC over CIP with file-based addressing.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number. Default 0.</param>
    /// <param name="options">Optional connection options.</param>
    public static SlcDriver CreateMicroLogix(
        string host, byte slot = 0, ConnectionOptions? options = null)
    {
        return new SlcDriver(host, slot, SlcPlcType.MicroLogix, options);
    }

    /// <summary>
    /// Create a driver for Allen-Bradley PLC-5 over Ethernet.
    /// Uses PCCC over CIP with file-based addressing.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number. Default 0.</param>
    /// <param name="options">Optional connection options.</param>
    public static SlcDriver CreatePlc5(
        string host, byte slot = 0, ConnectionOptions? options = null)
    {
        return new SlcDriver(host, slot, SlcPlcType.Plc5, options);
    }
}
