using SimplePLCDriverCore.Common.Transport;

namespace SimplePLCDriverCore.Protocols.Modbus;

/// <summary>
/// Manages a Modbus TCP session.
///
/// Connection flow:
///   1. TCP connect to port 502
///   2. Ready for Modbus requests (no handshake needed)
///
/// Modbus TCP is stateless at the application layer - each request/response
/// is independent. The MBAP header contains a transaction ID for matching.
/// </summary>
internal sealed class ModbusSession : IAsyncDisposable
{
    public const int DefaultPort = 502;

    private readonly ITransport _transport;
    private readonly byte _unitId;
    private ushort _transactionId;
    private bool _connected;

    public bool IsConnected => _connected && _transport.IsConnected;
    public byte UnitId => _unitId;

    public ModbusSession(ITransport transport, byte unitId = 1)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _unitId = unitId;
    }

    /// <summary>
    /// Mark the session as connected. Modbus TCP has no application-level handshake.
    /// </summary>
    public void MarkConnected()
    {
        _connected = true;
    }

    /// <summary>
    /// Send a Modbus request and receive the response.
    /// </summary>
    public async ValueTask<ModbusResponse> SendAsync(byte[] request, CancellationToken ct = default)
    {
        await _transport.SendAsync(request, ct).ConfigureAwait(false);

        var response = await _transport.ReceiveFramedAsync(
            6, // Read first 6 bytes of MBAP header (transaction ID + protocol ID + length)
            ModbusMessage.GetLengthFromHeader,
            ct).ConfigureAwait(false);

        return ModbusMessage.ParseResponse(response);
    }

    public ushort GetNextTransactionId() => ++_transactionId;

    public async ValueTask DisposeAsync()
    {
        _connected = false;
    }
}
