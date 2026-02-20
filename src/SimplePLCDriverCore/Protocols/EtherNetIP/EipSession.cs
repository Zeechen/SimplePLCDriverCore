using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Protocols.EtherNetIP;

/// <summary>
/// Manages an EtherNet/IP session with a PLC, including:
///   - TCP connection
///   - EtherNet/IP session registration
///   - CIP connection (Forward Open)
///   - Sending/receiving CIP messages (connected and unconnected)
///   - Connection sequence number tracking
/// </summary>
internal sealed class EipSession : IAsyncDisposable, IDisposable
{
    private readonly ITransport _transport;
    private readonly byte _slot;
    private readonly ushort _vendorId;
    private readonly uint _originatorSerial;
    private readonly SemaphoreSlim _transactionLock = new(1, 1);

    // Session state
    private uint _sessionHandle;
    private bool _sessionRegistered;

    // CIP connection state
    private uint _otConnectionId;    // Originator-to-Target (our ID for the connection)
    private uint _toConnectionId;    // Target-to-Originator (PLC's connection ID for us)
    private ushort _connectionSerial;
    private ushort _sequenceNumber;
    private bool _cipConnected;
    private int _connectionSize;     // Negotiated CIP connection data size

    public bool IsSessionRegistered => _sessionRegistered;
    public bool IsCipConnected => _cipConnected;
    public bool IsConnected => _transport.IsConnected && _sessionRegistered;
    public uint SessionHandle => _sessionHandle;
    public int ConnectionSize => _connectionSize;

    public EipSession(ITransport transport, byte slot = 0, ushort vendorId = 0x0001)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _slot = slot;
        _vendorId = vendorId;
        _originatorSerial = (uint)(Environment.TickCount & 0x7FFFFFFF);
    }

    /// <summary>
    /// Register an EtherNet/IP session with the PLC.
    /// Must be called after the transport is connected.
    /// </summary>
    public async ValueTask RegisterSessionAsync(CancellationToken ct = default)
    {
        if (_sessionRegistered)
            return;

        var request = EipEncapsulation.BuildRegisterSession();
        var response = await SendReceiveRawAsync(request, ct).ConfigureAwait(false);

        var (header, _) = EipEncapsulation.Decode(response);

        if (header.Status != EipStatus.Success)
            throw new IOException($"RegisterSession failed: {header.Status}");

        if (header.SessionHandle == 0)
            throw new IOException("RegisterSession returned invalid session handle (0)");

        _sessionHandle = header.SessionHandle;
        _sessionRegistered = true;
    }

    /// <summary>
    /// Unregister the EtherNet/IP session.
    /// </summary>
    public async ValueTask UnregisterSessionAsync(CancellationToken ct = default)
    {
        if (!_sessionRegistered)
            return;

        try
        {
            var request = EipEncapsulation.BuildUnregisterSession(_sessionHandle);
            await _transport.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort - connection may already be closed
        }

        _sessionHandle = 0;
        _sessionRegistered = false;
    }

    /// <summary>
    /// Establish a CIP connection via Large Forward Open.
    /// Falls back to standard Forward Open if Large is not supported.
    /// </summary>
    public async ValueTask ForwardOpenAsync(CancellationToken ct = default)
    {
        if (_cipConnected)
            return;

        if (!_sessionRegistered)
            throw new InvalidOperationException("Session must be registered before Forward Open");

        // Try Large Forward Open first (4002 byte connection size)
        try
        {
            var (request, serial) = ForwardOpen.BuildLargeForwardOpen(
                _slot, _originatorSerial, _vendorId);

            var cipResponse = await SendUnconnectedAsync(request, ct).ConfigureAwait(false);

            if (!cipResponse.IsSuccess)
                throw new IOException($"Large Forward Open failed: {cipResponse.GetErrorMessage()}");

            var result = Cip.ForwardOpen.ParseResponse(cipResponse.Data);
            _otConnectionId = result.OtoTConnectionId;
            _toConnectionId = result.TtoOConnectionId;
            _connectionSerial = serial;
            _connectionSize = 4002;
            _sequenceNumber = 0;
            _cipConnected = true;
            return;
        }
        catch (IOException)
        {
            // Large Forward Open not supported, try standard
        }

        // Fallback: Standard Forward Open (504 byte connection size)
        {
            var (request, serial) = ForwardOpen.BuildForwardOpen(
                _slot, _originatorSerial, _vendorId);

            var cipResponse = await SendUnconnectedAsync(request, ct).ConfigureAwait(false);

            if (!cipResponse.IsSuccess)
                throw new IOException($"Forward Open failed: {cipResponse.GetErrorMessage()}");

            var result = Cip.ForwardOpen.ParseResponse(cipResponse.Data);
            _otConnectionId = result.OtoTConnectionId;
            _toConnectionId = result.TtoOConnectionId;
            _connectionSerial = serial;
            _connectionSize = 504;
            _sequenceNumber = 0;
            _cipConnected = true;
        }
    }

    /// <summary>
    /// Close the CIP connection via Forward Close.
    /// </summary>
    public async ValueTask ForwardCloseAsync(CancellationToken ct = default)
    {
        if (!_cipConnected)
            return;

        try
        {
            var request = Cip.ForwardOpen.BuildForwardClose(
                _connectionSerial, _vendorId, _originatorSerial, _slot);

            await SendUnconnectedAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort
        }

        _cipConnected = false;
        _otConnectionId = 0;
        _toConnectionId = 0;
    }

    /// <summary>
    /// Send a CIP request over the connected transport (SendUnitData)
    /// and receive the CIP response.
    /// This is the primary method for tag read/write operations.
    /// </summary>
    public async ValueTask<CipResponse> SendConnectedAsync(
        ReadOnlyMemory<byte> cipRequest, CancellationToken ct = default)
    {
        if (!_cipConnected)
            throw new InvalidOperationException("CIP connection not established. Call ForwardOpenAsync first.");

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var seq = ++_sequenceNumber;
            var eipPacket = EipEncapsulation.BuildSendUnitData(
                _sessionHandle, _otConnectionId, seq, cipRequest.Span);

            var response = await SendReceiveRawAsync(eipPacket, ct).ConfigureAwait(false);

            var (header, eipData) = EipEncapsulation.Decode(response);

            if (header.Status != EipStatus.Success)
                throw new IOException($"SendUnitData failed: {header.Status}");

            var cipData = EipEncapsulation.ExtractCipData(eipData, isConnected: true);
            return CipMessage.ParseResponse(cipData);
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>
    /// Send a CIP request using unconnected messaging (SendRRData).
    /// Used for Forward Open/Close and other non-connected operations.
    /// </summary>
    public async ValueTask<CipResponse> SendUnconnectedAsync(
        ReadOnlyMemory<byte> cipRequest, CancellationToken ct = default)
    {
        if (!_sessionRegistered)
            throw new InvalidOperationException("Session not registered");

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var eipPacket = EipEncapsulation.BuildSendRRData(
                _sessionHandle, cipRequest.Span);

            var response = await SendReceiveRawAsync(eipPacket, ct).ConfigureAwait(false);

            var (header, eipData) = EipEncapsulation.Decode(response);

            if (header.Status != EipStatus.Success)
                throw new IOException($"SendRRData failed: {header.Status}");

            var cipData = EipEncapsulation.ExtractCipData(eipData, isConnected: false);
            return CipMessage.ParseResponse(cipData);
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>
    /// Send a CIP request using unconnected messaging, wrapped in an Unconnected Send
    /// for routing through the backplane to the PLC processor.
    /// </summary>
    public async ValueTask<CipResponse> SendRoutedUnconnectedAsync(
        ReadOnlyMemory<byte> cipRequest, CancellationToken ct = default)
    {
        var wrappedRequest = CipMessage.BuildUnconnectedSend(cipRequest.Span, _slot);
        return await SendUnconnectedAsync(wrappedRequest, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Low-level send/receive that handles EtherNet/IP framing.
    /// Sends a complete EIP message and reads the complete response.
    /// </summary>
    private async ValueTask<byte[]> SendReceiveRawAsync(byte[] request, CancellationToken ct)
    {
        await _transport.SendAsync(request, ct).ConfigureAwait(false);

        // Use framed receive: read 24-byte header first, then payload
        return await _transport.ReceiveFramedAsync(
            EipConstants.EncapsulationHeaderSize,
            EipEncapsulation.GetTotalLengthFromHeader,
            ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ForwardCloseAsync().ConfigureAwait(false);
        await UnregisterSessionAsync().ConfigureAwait(false);
        _transactionLock.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
