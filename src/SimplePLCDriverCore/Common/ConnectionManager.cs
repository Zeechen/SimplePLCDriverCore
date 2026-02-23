using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.EtherNetIP;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Common;

/// <summary>
/// Manages the full PLC connection lifecycle including:
///   - TCP connect + EIP session + CIP connection
///   - Keepalive timer to prevent CIP connection timeout
///   - Auto-reconnect on connection loss
///   - Thread-safe CIP request dispatching
/// </summary>
internal sealed class ConnectionManager : IAsyncDisposable, IDisposable
{
    private readonly string _host;
    private readonly ConnectionOptions _options;
    private readonly Func<ITransport> _transportFactory;

    private ITransport? _transport;
    private EipSession? _session;
    private Timer? _keepAliveTimer;
    private bool _disposed;
    private int _reconnecting; // 0 = not reconnecting, 1 = reconnecting

    public bool IsConnected => _session?.IsConnected == true && _session.IsCipConnected;
    public int ConnectionSize => _session?.ConnectionSize ?? 0;

    public ConnectionManager(string host, ConnectionOptions? options = null)
        : this(host, options, null)
    {
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal ConnectionManager(
        string host, ConnectionOptions? options, Func<ITransport>? transportFactory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory ??
            (() => new TcpTransport(host, EipConstants.DefaultPort, _options.ConnectTimeout));
    }

    /// <summary>
    /// Establish the full connection: TCP -> EIP Session -> CIP Forward Open.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        await ConnectInternalAsync(ct).ConfigureAwait(false);
        StartKeepAlive();
    }

    /// <summary>
    /// Gracefully disconnect: Forward Close -> Unregister Session -> TCP close.
    /// </summary>
    public async ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        StopKeepAlive();

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

    /// <summary>
    /// Send a CIP request over the connected transport and receive the response.
    /// Handles auto-reconnect on connection loss if configured.
    /// </summary>
    public async ValueTask<CipResponse> SendAsync(
        ReadOnlyMemory<byte> cipRequest, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_options.RequestTimeout > TimeSpan.Zero)
            timeoutCts.CancelAfter(_options.RequestTimeout);
        var token = timeoutCts.Token;

        try
        {
            return await session.SendConnectedAsync(cipRequest, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"CIP request timed out after {_options.RequestTimeout.TotalSeconds:F0}s");
        }
        catch (IOException) when (_options.AutoReconnect)
        {
            await ReconnectAsync(ct).ConfigureAwait(false);
            session = _session ?? throw new IOException("Reconnect failed");
            return await session.SendConnectedAsync(cipRequest, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Send a CIP request using unconnected messaging (for discovery operations).
    /// </summary>
    public async ValueTask<CipResponse> SendUnconnectedAsync(
        ReadOnlyMemory<byte> cipRequest, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_options.RequestTimeout > TimeSpan.Zero)
            timeoutCts.CancelAfter(_options.RequestTimeout);

        try
        {
            return await session.SendRoutedUnconnectedAsync(cipRequest, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"CIP request timed out after {_options.RequestTimeout.TotalSeconds:F0}s");
        }
    }

    /// <summary>
    /// Send multiple CIP requests as a batch using Multiple Service Packet.
    /// Auto-splits into multiple packets if they exceed the connection size.
    /// Returns one CipResponse per request, in the same order.
    /// </summary>
    public async ValueTask<CipResponse[]> SendBatchAsync(
        IReadOnlyList<byte[]> cipRequests, CancellationToken ct = default)
    {
        if (cipRequests.Count == 0)
            return [];

        if (cipRequests.Count == 1)
        {
            var single = await SendAsync(cipRequests[0], ct).ConfigureAwait(false);
            return [single];
        }

        var maxSize = _session?.ConnectionSize ?? 504;
        var groups = MultiServicePacket.SplitIntoGroups(cipRequests, maxSize);

        var allResponses = new CipResponse[cipRequests.Count];

        foreach (var group in groups)
        {
            // Build the requests for this group
            var groupRequests = new byte[group.Count][];
            for (var i = 0; i < group.Count; i++)
                groupRequests[i] = cipRequests[group[i]];

            var batchPacket = MultiServicePacket.Build(groupRequests);
            var batchResponse = await SendAsync(batchPacket, ct).ConfigureAwait(false);

            if (!batchResponse.IsSuccess)
            {
                // Entire batch failed - fill all responses with the error
                for (var i = 0; i < group.Count; i++)
                {
                    allResponses[group[i]] = new CipResponse(
                        CipServices.MultipleServicePacket,
                        batchResponse.GeneralStatus,
                        batchResponse.AdditionalStatus,
                        ReadOnlyMemory<byte>.Empty);
                }
                continue;
            }

            // Parse individual responses from the batch
            CipResponse[] groupResponses;
            if (group.Count == 1)
            {
                // Single request wasn't wrapped, response is direct
                groupResponses = [batchResponse];
            }
            else
            {
                groupResponses = MultiServicePacket.ParseResponse(batchResponse.Data);
            }

            for (var i = 0; i < group.Count && i < groupResponses.Length; i++)
                allResponses[group[i]] = groupResponses[i];
        }

        return allResponses;
    }

    // --- Connection Lifecycle ---

    private async ValueTask ConnectInternalAsync(CancellationToken ct)
    {
        _transport = _transportFactory();
        await _transport.ConnectAsync(ct).ConfigureAwait(false);

        _session = new EipSession(_transport, _options.Slot);
        await _session.RegisterSessionAsync(ct).ConfigureAwait(false);
        await _session.ForwardOpenAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask ReconnectAsync(CancellationToken ct)
    {
        // Prevent concurrent reconnection attempts
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            return;

        try
        {
            StopKeepAlive();

            var policy = _options.GetEffectiveReconnectPolicy();

            try
            {
                await policy.ExecuteAsync(async (attempt, token) =>
                {
                    // Clean up old connection
                    if (_session != null)
                    {
                        try { await _session.DisposeAsync().ConfigureAwait(false); }
                        catch { /* ignore cleanup errors */ }
                        _session = null;
                    }
                    if (_transport != null)
                    {
                        try { await _transport.DisposeAsync().ConfigureAwait(false); }
                        catch { /* ignore cleanup errors */ }
                        _transport = null;
                    }

                    await ConnectInternalAsync(token).ConfigureAwait(false);
                    StartKeepAlive();
                }, ct).ConfigureAwait(false);
            }
            catch (RetryExhaustedException ex)
            {
                throw new IOException(
                    $"Failed to reconnect to {_host} after {policy.MaxAttempts} attempts",
                    ex.InnerException);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    // --- Keepalive ---

    private void StartKeepAlive()
    {
        if (_options.KeepAliveInterval <= TimeSpan.Zero)
            return;

        _keepAliveTimer = new Timer(
            KeepAliveCallback,
            null,
            _options.KeepAliveInterval,
            _options.KeepAliveInterval);
    }

    private void StopKeepAlive()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;
    }

    private async void KeepAliveCallback(object? state)
    {
        if (!IsConnected)
            return;

        try
        {
            // Send a no-op GetAttributeList to the Identity Object to keep the connection alive
            var keepAliveRequest = CipMessage.BuildGetAttributeListRequest(
                CipClasses.Identity, 1, [1]); // attribute 1 = vendor ID (small response)

            await SendAsync(keepAliveRequest, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Keepalive failed - connection may be lost
            // If AutoReconnect is enabled, the next real request will trigger reconnection
        }
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
}
