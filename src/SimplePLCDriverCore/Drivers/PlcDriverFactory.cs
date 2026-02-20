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
}
