using System.Collections.Concurrent;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Drivers;

namespace SimplePLCDriverCore.Common;

/// <summary>
/// Manages a pool of named PLC driver connections.
/// Drivers are created lazily on first access and disposed together.
///
/// Usage:
///   var pool = new ConnectionPool();
///   pool.Register("PLC1", "192.168.1.100");
///   pool.Register("PLC2", "192.168.1.101", slot: 2);
///
///   var plc1 = await pool.GetAsync("PLC1");
///   var result = await plc1.ReadAsync("MyTag");
///
///   await pool.DisposeAsync(); // disconnects all
/// </summary>
public sealed class ConnectionPool : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, Registration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPlcDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Register a Logix PLC (ControlLogix/CompactLogix) with the pool.
    /// </summary>
    /// <param name="name">Unique name to identify this connection.</param>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number.</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    public void Register(string name, string host, byte slot = 0, ConnectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(host);
        ThrowIfDisposed();

        var registration = new Registration(PlcType.Logix, host, slot, options);
        if (!_registrations.TryAdd(name, registration))
            throw new InvalidOperationException($"A connection named '{name}' is already registered.");
    }

    /// <summary>
    /// Get a connected driver by name. Creates and connects on first access.
    /// </summary>
    /// <param name="name">The registered connection name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected IPlcDriver instance.</returns>
    public async ValueTask<IPlcDriver> GetAsync(string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ThrowIfDisposed();

        // Fast path: already connected
        if (_drivers.TryGetValue(name, out var existing) && existing.IsConnected)
            return existing;

        // Slow path: create and connect under lock
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_drivers.TryGetValue(name, out existing) && existing.IsConnected)
                return existing;

            if (!_registrations.TryGetValue(name, out var registration))
                throw new KeyNotFoundException($"No connection named '{name}' is registered.");

            // Dispose old disconnected driver if it exists
            if (existing != null)
            {
                try { await existing.DisposeAsync().ConfigureAwait(false); }
                catch { /* ignore cleanup errors */ }
            }

            var driver = CreateDriver(registration);
            await driver.ConnectAsync(ct).ConfigureAwait(false);
            _drivers[name] = driver;
            return driver;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Check if a named connection is currently connected.
    /// </summary>
    public bool IsConnected(string name)
    {
        return _drivers.TryGetValue(name, out var driver) && driver.IsConnected;
    }

    /// <summary>
    /// Get all registered connection names.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredNames => _registrations.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Get all currently connected connection names.
    /// </summary>
    public IReadOnlyCollection<string> ConnectedNames =>
        _drivers.Where(kv => kv.Value.IsConnected).Select(kv => kv.Key).ToList().AsReadOnly();

    /// <summary>
    /// Disconnect and remove a specific named connection.
    /// The registration is preserved — calling GetAsync will reconnect.
    /// </summary>
    public async ValueTask DisconnectAsync(string name, CancellationToken ct = default)
    {
        if (_drivers.TryRemove(name, out var driver))
        {
            await driver.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Remove a registration and disconnect its driver if connected.
    /// </summary>
    public async ValueTask UnregisterAsync(string name, CancellationToken ct = default)
    {
        _registrations.TryRemove(name, out _);
        if (_drivers.TryRemove(name, out var driver))
        {
            await driver.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disconnect all active connections. Registrations are preserved.
    /// </summary>
    public async ValueTask DisconnectAllAsync(CancellationToken ct = default)
    {
        var drivers = _drivers.Values.ToList();
        _drivers.Clear();

        foreach (var driver in drivers)
        {
            try { await driver.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort cleanup */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await DisconnectAllAsync().ConfigureAwait(false);
        _lock.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var driver in _drivers.Values)
        {
            try { driver.Dispose(); }
            catch { /* best-effort cleanup */ }
        }
        _drivers.Clear();
        _lock.Dispose();
    }

    private static IPlcDriver CreateDriver(Registration registration)
    {
        return registration.Type switch
        {
            PlcType.Logix => new LogixDriver(
                registration.Host, registration.Slot, registration.Options),
            _ => throw new NotSupportedException(
                $"PLC type '{registration.Type}' is not yet supported.")
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record Registration(
        PlcType Type, string Host, byte Slot, ConnectionOptions? Options);

    internal enum PlcType
    {
        Logix,
        // Future: Slc, Siemens, Omron, Modbus
    }
}
