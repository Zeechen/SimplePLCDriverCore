namespace SimplePLCDriverCore.Common;

/// <summary>
/// Configuration options for PLC connections.
/// </summary>
public sealed class ConnectionOptions
{
    /// <summary>TCP connect timeout.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Timeout for individual CIP requests.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Interval for sending keepalive messages to prevent CIP connection timeout.
    /// Set to Zero to disable keepalive. Default is 30 seconds.
    /// The CIP connection RPI is typically 2 seconds with a timeout multiplier of 8,
    /// giving a 16-second window. 30s is conservative enough for most configurations
    /// but we send an empty read to keep it alive.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether to automatically reconnect on connection loss.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Maximum number of auto-reconnect attempts before giving up.</summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>Delay between reconnect attempts.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// PLC processor slot on the backplane.
    /// ControlLogix typically uses slot 0 or the slot where the processor module resides.
    /// CompactLogix is always slot 0.
    /// </summary>
    public byte Slot { get; set; } = 0;

    /// <summary>
    /// CIP connection size in bytes. 0 = auto-negotiate (try Large Forward Open 4002 first).
    /// </summary>
    public int ConnectionSize { get; set; } = 0;
}
