using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.Fins;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// High-level driver for Omron PLCs using FINS (Factory Interface Network Service) over TCP.
/// Supports Omron NJ, NX, CJ, CP, and CS series PLCs.
///
/// Addressing uses Omron memory area notation:
///   D0       - DM area, word 0
///   D100     - DM area, word 100
///   D100.0   - DM area, word 100, bit 0
///   CIO0     - CIO area, word 0
///   CIO0.00  - CIO area, word 0, bit 0
///   W0       - Work area, word 0
///   H0       - Holding area, word 0
///   A0       - Auxiliary area, word 0
///   T0       - Timer PV, number 0
///   C0       - Counter PV, number 0
///
/// Usage:
///   await using var plc = new OmronDriver("192.168.1.100");
///   await plc.ConnectAsync();
///   var result = await plc.ReadAsync("D100");
///   await plc.WriteAsync("D100", (short)42);
/// </summary>
public sealed class OmronDriver : IPlcDriver
{
    private readonly string _host;
    private readonly ConnectionOptions _options;
    private readonly Func<ITransport>? _transportFactory;

    private ITransport? _transport;
    private FinsSession? _session;
    private bool _disposed;

    public bool IsConnected => _session?.IsConnected == true;

    /// <summary>
    /// Create an OmronDriver for the specified PLC host.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    public OmronDriver(string host, ConnectionOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new ConnectionOptions();
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal OmronDriver(string host, ConnectionOptions? options,
        Func<ITransport>? transportFactory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory;
    }

    /// <summary>
    /// Connect to the PLC: TCP + FINS/TCP node address handshake.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _transport = _transportFactory != null
            ? _transportFactory()
            : new TcpTransport(_host, FinsSession.DefaultPort, _options.ConnectTimeout);

        await _transport.ConnectAsync(ct).ConfigureAwait(false);

        _session = new FinsSession(_transport);
        await _session.ConnectAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnect from the PLC.
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
    /// Read a single FINS address.
    /// </summary>
    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = FinsAddress.Parse(tagName);
            var sid = _session!.GetNextSid();

            var request = FinsMessage.BuildReadRequest(
                address, sid, _session.ClientNode, _session.ServerNode);

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "FINS read failed", response.GetErrorMessage());

            if (response.Data.Length == 0)
                return TagResult.Failure(tagName, "FINS read returned no data");

            var value = FinsTypes.DecodeWord(response.Data.Span, address);
            var typeName = FinsTypes.GetTypeName(address);

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
    /// Write a single FINS address.
    /// </summary>
    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = FinsAddress.Parse(tagName);
            var data = FinsTypes.EncodeWord(value, address);
            var sid = _session!.GetNextSid();

            var request = FinsMessage.BuildWriteRequest(
                address, data, sid, _session.ClientNode, _session.ServerNode);

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "FINS write failed", response.GetErrorMessage());

            var typeName = FinsTypes.GetTypeName(address);
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
    /// Read multiple FINS addresses. Each address is sent individually
    /// (FINS supports multi-read but implementation complexity is deferred).
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
    /// Write multiple FINS addresses. Each address is sent individually.
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

    // --- IPlcDriver: JSON/Typed operations (not supported for FINS) ---

    public ValueTask<TagResult<string>> ReadJsonAsync(string tagName, CancellationToken ct = default)
    {
        return new ValueTask<TagResult<string>>(
            TagResult<string>.Failure(tagName,
                "ReadJsonAsync is not supported for Omron FINS PLCs. Use ReadAsync instead."));
    }

    public ValueTask<TagResult<T>> ReadAsync<T>(string tagName, CancellationToken ct = default) where T : class, new()
    {
        return new ValueTask<TagResult<T>>(
            TagResult<T>.Failure(tagName,
                "Typed ReadAsync<T> is not supported for Omron FINS PLCs. Use ReadAsync instead."));
    }

    public ValueTask<TagResult> WriteAsync<T>(string tagName, T value, CancellationToken ct = default) where T : class
    {
        if (value is string)
            return WriteAsync(tagName, (object)value, ct);

        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "Typed WriteAsync<T> is not supported for Omron FINS PLCs. Use WriteAsync instead."));
    }

    public ValueTask<TagResult> WriteJsonAsync(string tagName, string json, CancellationToken ct = default)
    {
        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "WriteJsonAsync is not supported for Omron FINS PLCs. Use WriteAsync instead."));
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
