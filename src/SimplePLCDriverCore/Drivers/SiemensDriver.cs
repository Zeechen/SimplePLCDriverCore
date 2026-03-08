using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.S7;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// High-level driver for Siemens S7-300, S7-400, S7-1200, and S7-1500 PLCs.
/// Uses the S7comm protocol over ISO-on-TCP (TPKT + COTP + S7).
///
/// Addressing uses the standard S7 notation:
///   DB1.DBX0.0  - Data Block 1, bit at byte 0, bit 0
///   DB1.DBB0    - Data Block 1, byte at byte 0
///   DB1.DBW0    - Data Block 1, word (INT) at byte 0
///   DB1.DBD0    - Data Block 1, double word (DINT/REAL) at byte 0
///   DB1.DBS0.20 - Data Block 1, string at byte 0, max length 20
///   I0.0        - Input bit 0.0
///   IB0, IW0, ID0 - Input byte/word/dword
///   Q0.0        - Output bit 0.0
///   M0.0        - Merker bit 0.0
///   MB0, MW0, MD0 - Merker byte/word/dword
///   T0, C0      - Timer 0, Counter 0
///
/// Usage:
///   await using var plc = new SiemensDriver("192.168.1.200");
///   await plc.ConnectAsync();
///   var result = await plc.ReadAsync("DB1.DBW0");
///   await plc.WriteAsync("DB1.DBD4", 3.14f);
/// </summary>
public sealed class SiemensDriver : IPlcDriver
{
    private readonly string _host;
    private readonly byte _rack;
    private readonly byte _slot;
    private readonly ConnectionOptions _options;
    private readonly Func<ITransport>? _transportFactory;

    private ITransport? _transport;
    private S7Session? _session;
    private bool _disposed;

    public bool IsConnected => _session?.IsConnected == true;

    /// <summary>
    /// Create a SiemensDriver for the specified PLC host.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="rack">Rack number (typically 0).</param>
    /// <param name="slot">Slot number (typically 0 for S7-1200/1500, 2 for S7-300/400).</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    public SiemensDriver(string host, byte rack = 0, byte slot = 0,
        ConnectionOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _rack = rack;
        _slot = slot;
        _options = options ?? new ConnectionOptions();
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal SiemensDriver(string host, byte rack, byte slot,
        ConnectionOptions? options, Func<ITransport>? transportFactory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _rack = rack;
        _slot = slot;
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory;
    }

    /// <summary>
    /// Connect to the PLC: TCP + COTP handshake + S7 Communication Setup.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _transport = _transportFactory != null
            ? _transportFactory()
            : new TcpTransport(_host, S7Session.DefaultPort, _options.ConnectTimeout);

        await _transport.ConnectAsync(ct).ConfigureAwait(false);

        _session = new S7Session(_transport, _rack, _slot);
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
    /// Read a single S7 address.
    /// </summary>
    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = S7Address.Parse(tagName);
            var request = S7Message.BuildReadRequest(
                _session!.GetNextPduReference(), [address]);

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "S7 read failed", response.GetErrorMessage());

            if (response.ItemData.Length == 0)
                return TagResult.Failure(tagName, "S7 read returned no data");

            if (response.ItemReturnCodes[0] != 0xFF)
                return TagResult.Failure(tagName,
                    S7Response.GetItemErrorMessage(response.ItemReturnCodes[0]));

            var value = S7Types.DecodeValue(response.ItemData[0], address);
            var typeName = S7Types.GetTypeName(address);

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
    /// Write a single S7 address.
    /// </summary>
    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = S7Address.Parse(tagName);
            var data = S7Types.EncodeValue(value, address);

            var request = S7Message.BuildWriteRequest(
                _session!.GetNextPduReference(), address, data);

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "S7 write failed", response.GetErrorMessage());

            if (response.ItemReturnCodes.Length > 0 && response.ItemReturnCodes[0] != 0xFF)
                return TagResult.Failure(tagName,
                    S7Response.GetItemErrorMessage(response.ItemReturnCodes[0]));

            var typeName = S7Types.GetTypeName(address);
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
    /// Read multiple S7 addresses. Uses S7 multi-item read when possible.
    /// </summary>
    public async ValueTask<TagResult[]> ReadAsync(
        IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        EnsureConnected();

        var nameList = tagNames as IReadOnlyList<string> ?? tagNames.ToList();
        var results = new TagResult[nameList.Count];

        // Parse all addresses first
        var addresses = new S7Address[nameList.Count];
        var parseErrors = new bool[nameList.Count];

        for (int i = 0; i < nameList.Count; i++)
        {
            try
            {
                addresses[i] = S7Address.Parse(nameList[i]);
            }
            catch (FormatException ex)
            {
                results[i] = TagResult.Failure(nameList[i], $"Invalid address: {ex.Message}");
                parseErrors[i] = true;
            }
        }

        // Build list of valid addresses for batch read
        var validIndices = new List<int>();
        for (int i = 0; i < nameList.Count; i++)
        {
            if (!parseErrors[i])
                validIndices.Add(i);
        }

        if (validIndices.Count == 0)
            return results;

        // S7 supports multi-item reads (up to PDU size limit)
        // Split into chunks based on PDU size (conservative: ~20 items per request)
        const int maxItemsPerRequest = 20;

        for (int chunk = 0; chunk < validIndices.Count; chunk += maxItemsPerRequest)
        {
            var chunkIndices = validIndices.Skip(chunk).Take(maxItemsPerRequest).ToList();
            var chunkAddresses = chunkIndices.Select(i => addresses[i]).ToArray();

            try
            {
                var request = S7Message.BuildReadRequest(
                    _session!.GetNextPduReference(), chunkAddresses);
                var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccess)
                {
                    foreach (var i in chunkIndices)
                        results[i] = TagResult.Failure(nameList[i], "S7 batch read failed",
                            response.GetErrorMessage());
                    continue;
                }

                for (int j = 0; j < chunkIndices.Count; j++)
                {
                    var idx = chunkIndices[j];
                    if (j >= response.ItemData.Length)
                    {
                        results[idx] = TagResult.Failure(nameList[idx], "No data returned for item");
                        continue;
                    }

                    if (response.ItemReturnCodes[j] != 0xFF)
                    {
                        results[idx] = TagResult.Failure(nameList[idx],
                            S7Response.GetItemErrorMessage(response.ItemReturnCodes[j]));
                        continue;
                    }

                    try
                    {
                        var value = S7Types.DecodeValue(response.ItemData[j], addresses[idx]);
                        results[idx] = TagResult.Success(nameList[idx], value,
                            S7Types.GetTypeName(addresses[idx]));
                    }
                    catch (Exception ex)
                    {
                        results[idx] = TagResult.Failure(nameList[idx], ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var i in chunkIndices)
                    results[i] = TagResult.Failure(nameList[i], ex.Message);
            }
        }

        return results;
    }

    /// <summary>
    /// Write multiple S7 addresses. Each write is sent individually
    /// (S7 multi-item write has limited support on some models).
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

    // --- IPlcDriver: JSON/Typed operations (not supported for S7 basic addressing) ---

    public ValueTask<TagResult<string>> ReadJsonAsync(string tagName, CancellationToken ct = default)
    {
        return new ValueTask<TagResult<string>>(
            TagResult<string>.Failure(tagName,
                "ReadJsonAsync is not supported for Siemens S7 PLCs. Use ReadAsync instead."));
    }

    public ValueTask<TagResult<T>> ReadAsync<T>(string tagName, CancellationToken ct = default) where T : class, new()
    {
        return new ValueTask<TagResult<T>>(
            TagResult<T>.Failure(tagName,
                "Typed ReadAsync<T> is not supported for Siemens S7 PLCs. Use ReadAsync instead."));
    }

    public ValueTask<TagResult> WriteAsync<T>(string tagName, T value, CancellationToken ct = default) where T : class
    {
        if (value is string)
            return WriteAsync(tagName, (object)value, ct);

        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "Typed WriteAsync<T> is not supported for Siemens S7 PLCs. Use WriteAsync instead."));
    }

    public ValueTask<TagResult> WriteJsonAsync(string tagName, string json, CancellationToken ct = default)
    {
        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "WriteJsonAsync is not supported for Siemens S7 PLCs. Use WriteAsync instead."));
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
