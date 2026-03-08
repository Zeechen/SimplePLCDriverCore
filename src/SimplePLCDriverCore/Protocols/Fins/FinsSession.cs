using SimplePLCDriverCore.Common.Transport;

namespace SimplePLCDriverCore.Protocols.Fins;

/// <summary>
/// Manages the FINS/TCP session including the initial node address handshake.
///
/// Connection flow:
///   1. TCP connect to port 9600
///   2. FINS/TCP node address exchange (client sends request, PLC responds with node assignments)
///   3. Ready for FINS command frames
/// </summary>
internal sealed class FinsSession : IAsyncDisposable
{
    public const int DefaultPort = 9600;

    private readonly ITransport _transport;
    private byte _clientNode;
    private byte _serverNode;
    private byte _sid;
    private bool _connected;

    public bool IsConnected => _connected && _transport.IsConnected;
    public byte ClientNode => _clientNode;
    public byte ServerNode => _serverNode;

    public FinsSession(ITransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Perform the FINS/TCP node address handshake.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        // Send node address request
        var request = FinsMessage.BuildNodeAddressRequest();
        await _transport.SendAsync(request, ct).ConfigureAwait(false);

        // Receive node address response
        var response = await _transport.ReceiveFramedAsync(
            8, // Read first 8 bytes (magic + length)
            FinsMessage.GetLengthFromHeader,
            ct).ConfigureAwait(false);

        var (clientNode, serverNode) = FinsMessage.ParseNodeAddressResponse(response);
        _clientNode = clientNode;
        _serverNode = serverNode;
        _connected = true;
    }

    /// <summary>
    /// Send a FINS command and receive the response.
    /// </summary>
    public async ValueTask<FinsResponse> SendAsync(byte[] finsFrame, CancellationToken ct = default)
    {
        await _transport.SendAsync(finsFrame, ct).ConfigureAwait(false);

        var response = await _transport.ReceiveFramedAsync(
            8, // Read first 8 bytes to get length
            FinsMessage.GetLengthFromHeader,
            ct).ConfigureAwait(false);

        return FinsMessage.ParseResponse(response);
    }

    public byte GetNextSid() => ++_sid;

    public async ValueTask DisposeAsync()
    {
        _connected = false;
    }
}
