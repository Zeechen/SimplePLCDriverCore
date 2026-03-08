using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// High-level driver for Modbus TCP devices.
/// Supports any device that speaks Modbus TCP (port 502).
///
/// Addressing uses prefix notation:
///   HR100    - Holding Register 100 (read/write, 16-bit)
///   IR100    - Input Register 100 (read-only, 16-bit)
///   C100     - Coil 100 (read/write, 1-bit)
///   DI100    - Discrete Input 100 (read-only, 1-bit)
///
///   Classic Modbus numeric addresses:
///   400001   - Holding Register 0
///   300001   - Input Register 0
///   100001   - Discrete Input 0
///   1        - Coil 0
///
/// Usage:
///   await using var device = new ModbusDriver("192.168.1.50");
///   await device.ConnectAsync();
///   var result = await device.ReadAsync("HR100");
///   await device.WriteAsync("HR100", (short)42);
/// </summary>
public sealed class ModbusDriver : IPlcDriver
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly ConnectionOptions _options;
    private readonly Func<ITransport>? _transportFactory;

    private ITransport? _transport;
    private ModbusSession? _session;
    private bool _disposed;

    public bool IsConnected => _session?.IsConnected == true;

    /// <summary>
    /// Create a ModbusDriver for the specified device.
    /// </summary>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="port">Modbus TCP port. Default 502.</param>
    /// <param name="unitId">Modbus unit/slave ID. Default 1.</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    public ModbusDriver(string host, int port = 502, byte unitId = 1,
        ConnectionOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _unitId = unitId;
        _options = options ?? new ConnectionOptions();
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal ModbusDriver(string host, int port, byte unitId,
        ConnectionOptions? options, Func<ITransport>? transportFactory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _unitId = unitId;
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory;
    }

    /// <summary>
    /// Connect to the Modbus TCP device. No application-level handshake needed.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _transport = _transportFactory != null
            ? _transportFactory()
            : new TcpTransport(_host, _port, _options.ConnectTimeout);

        await _transport.ConnectAsync(ct).ConfigureAwait(false);

        _session = new ModbusSession(_transport, _unitId);
        _session.MarkConnected();
    }

    /// <summary>
    /// Disconnect from the device.
    /// </summary>
    public async ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        if (_session != null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }

        if (_transport != null)
        {
            await _transport.DisconnectAsync(ct).ConfigureAwait(false);
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
        }
    }

    // --- IPlcDriver: Single Tag Operations ---

    /// <summary>
    /// Read a single Modbus address.
    /// </summary>
    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = ModbusAddress.Parse(tagName);
            var txId = _session!.GetNextTransactionId();

            var request = address.RegisterType switch
            {
                ModbusRegisterType.Coil =>
                    ModbusMessage.BuildReadCoils(txId, _unitId, (ushort)address.Address, 1),
                ModbusRegisterType.DiscreteInput =>
                    ModbusMessage.BuildReadDiscreteInputs(txId, _unitId, (ushort)address.Address, 1),
                ModbusRegisterType.HoldingRegister =>
                    ModbusMessage.BuildReadHoldingRegisters(txId, _unitId, (ushort)address.Address, 1),
                ModbusRegisterType.InputRegister =>
                    ModbusMessage.BuildReadInputRegisters(txId, _unitId, (ushort)address.Address, 1),
                _ => throw new InvalidOperationException($"Unsupported register type: {address.RegisterType}")
            };

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "Modbus read failed", response.GetErrorMessage());

            if (response.Data.Length == 0)
                return TagResult.Failure(tagName, "Modbus read returned no data");

            var value = ModbusTypes.DecodeValue(response.Data.Span, address);
            var typeName = ModbusTypes.GetTypeName(address);

            return TagResult.Success(tagName, value, typeName);
        }
        catch (FormatException ex)
        {
            return TagResult.Failure(tagName, $"Invalid address: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    /// <summary>
    /// Write a single Modbus address.
    /// </summary>
    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = ModbusAddress.Parse(tagName);
            var txId = _session!.GetNextTransactionId();

            byte[] request;

            if (address.IsBitRegister)
            {
                if (address.RegisterType == ModbusRegisterType.DiscreteInput)
                    return TagResult.Failure(tagName, "Cannot write to Discrete Inputs (read-only)");

                var boolValue = Convert.ToBoolean(value);
                request = ModbusMessage.BuildWriteSingleCoil(txId, _unitId,
                    (ushort)address.Address, boolValue);
            }
            else
            {
                if (address.RegisterType == ModbusRegisterType.InputRegister)
                    return TagResult.Failure(tagName, "Cannot write to Input Registers (read-only)");

                var regValue = ModbusTypes.EncodeRegister(value);
                request = ModbusMessage.BuildWriteSingleRegister(txId, _unitId,
                    (ushort)address.Address, regValue);
            }

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "Modbus write failed", response.GetErrorMessage());

            var typeName = ModbusTypes.GetTypeName(address);
            return TagResult.Success(tagName, PlcTagValue.Null, typeName);
        }
        catch (FormatException ex)
        {
            return TagResult.Failure(tagName, $"Invalid address: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    // --- IPlcDriver: Batch Operations ---

    /// <summary>
    /// Read multiple Modbus addresses. Each is sent individually.
    /// </summary>
    public async ValueTask<TagResult[]> ReadAsync(
        IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        var nameList = tagNames as IReadOnlyList<string> ?? tagNames.ToList();
        var results = new TagResult[nameList.Count];

        for (var i = 0; i < nameList.Count; i++)
        {
            results[i] = await ReadAsync(nameList[i], ct).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// Write multiple Modbus addresses. Each is sent individually.
    /// </summary>
    public async ValueTask<TagResult[]> WriteAsync(
        IEnumerable<(string TagName, object Value)> tags, CancellationToken ct = default)
    {
        var tagList = tags as IReadOnlyList<(string TagName, object Value)> ?? tags.ToList();
        var results = new TagResult[tagList.Count];

        for (var i = 0; i < tagList.Count; i++)
        {
            results[i] = await WriteAsync(tagList[i].TagName, tagList[i].Value, ct)
                .ConfigureAwait(false);
        }

        return results;
    }

    // --- IPlcDriver: JSON/Typed operations (not supported for Modbus) ---

    public ValueTask<TagResult<string>> ReadJsonAsync(string tagName, CancellationToken ct = default)
    {
        return new ValueTask<TagResult<string>>(
            TagResult<string>.Failure(tagName,
                "ReadJsonAsync is not supported for Modbus devices. Use ReadAsync instead."));
    }

    public ValueTask<TagResult<T>> ReadAsync<T>(string tagName, CancellationToken ct = default) where T : class, new()
    {
        return new ValueTask<TagResult<T>>(
            TagResult<T>.Failure(tagName,
                "Typed ReadAsync<T> is not supported for Modbus devices. Use ReadAsync instead."));
    }

    public ValueTask<TagResult> WriteAsync<T>(string tagName, T value, CancellationToken ct = default) where T : class
    {
        if (value is string)
            return WriteAsync(tagName, (object)value, ct);

        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "Typed WriteAsync<T> is not supported for Modbus devices. Use WriteAsync instead."));
    }

    public ValueTask<TagResult> WriteJsonAsync(string tagName, string json, CancellationToken ct = default)
    {
        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "WriteJsonAsync is not supported for Modbus devices. Use WriteAsync instead."));
    }

    // --- Disposal ---

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // --- Helpers ---

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }
}
