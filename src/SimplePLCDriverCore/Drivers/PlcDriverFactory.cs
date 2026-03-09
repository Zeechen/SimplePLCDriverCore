using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Protocols.Modbus;

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

    // --- Siemens S7 ---

    /// <summary>
    /// Create a driver for Siemens S7-1200 or S7-1500 PLCs (rack 0, slot 0).
    /// Uses S7comm over ISO-on-TCP with DB/I/Q/M addressing.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="options">Optional connection options.</param>
    public static SiemensDriver CreateS7_1200(
        string host, ConnectionOptions? options = null)
    {
        return new SiemensDriver(host, rack: 0, slot: 0, options);
    }

    /// <summary>
    /// Create a driver for Siemens S7-300 or S7-400 PLCs.
    /// Uses S7comm over ISO-on-TCP with DB/I/Q/M addressing.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="rack">Rack number (typically 0).</param>
    /// <param name="slot">Slot number (typically 2 for S7-300/400).</param>
    /// <param name="options">Optional connection options.</param>
    public static SiemensDriver CreateS7_300(
        string host, byte rack = 0, byte slot = 2, ConnectionOptions? options = null)
    {
        return new SiemensDriver(host, rack, slot, options);
    }

    /// <summary>
    /// Create a driver for any Siemens S7 PLC with explicit rack and slot.
    /// Uses S7comm over ISO-on-TCP with DB/I/Q/M addressing.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="rack">Rack number (typically 0).</param>
    /// <param name="slot">Slot number (0 for S7-1200/1500, 2 for S7-300/400).</param>
    /// <param name="options">Optional connection options.</param>
    public static SiemensDriver CreateSiemens(
        string host, byte rack = 0, byte slot = 0, ConnectionOptions? options = null)
    {
        return new SiemensDriver(host, rack, slot, options);
    }

    // --- Omron FINS ---

    /// <summary>
    /// Create a driver for Omron PLCs using FINS/TCP protocol.
    /// Supports NJ, NX, CJ, CP, and CS series with D/CIO/W/H/A addressing.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="options">Optional connection options.</param>
    public static OmronDriver CreateOmron(
        string host, ConnectionOptions? options = null)
    {
        return new OmronDriver(host, options);
    }

    // --- Modbus TCP ---

    /// <summary>
    /// Create a driver for any Modbus TCP device.
    /// Uses Modbus TCP (port 502) with HR/IR/C/DI addressing.
    /// </summary>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="port">Modbus TCP port. Default 502.</param>
    /// <param name="unitId">Modbus unit/slave ID. Default 1.</param>
    /// <param name="options">Optional connection options.</param>
    public static ModbusDriver CreateModbus(
        string host, int port = 502, byte unitId = 1, ConnectionOptions? options = null,
        ModbusByteOrder byteOrder = ModbusByteOrder.ABCD)
    {
        return new ModbusDriver(host, port, unitId, options, byteOrder);
    }

    /// <summary>
    /// Create a driver for a Modbus TCP device with default settings (port 502, unit ID 1).
    /// </summary>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="options">Optional connection options.</param>
    /// <param name="byteOrder">Default byte order for multi-register data types.</param>
    public static ModbusDriver CreateModbusTcp(
        string host, ConnectionOptions? options = null,
        ModbusByteOrder byteOrder = ModbusByteOrder.ABCD)
    {
        return new ModbusDriver(host, options: options, byteOrder: byteOrder);
    }
}
