using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Common.Transport;

namespace SimplePLCDriverCore.Protocols.S7;

/// <summary>
/// Manages the S7comm session: TPKT + COTP + S7 protocol layers.
///
/// Connection flow:
///   1. TCP connect to port 102
///   2. COTP Connection Request (CR) -> Connection Confirm (CC)
///   3. S7 Communication Setup -> negotiate PDU size
///   4. Ready for Read/Write operations
/// </summary>
internal sealed class S7Session : IAsyncDisposable
{
    public const int DefaultPort = 102;

    private readonly ITransport _transport;
    private readonly byte _rack;
    private readonly byte _slot;
    private ushort _pduReference;
    private ushort _negotiatedPduSize = 240;
    private bool _connected;

    public bool IsConnected => _connected && _transport.IsConnected;
    public ushort PduSize => _negotiatedPduSize;

    public S7Session(ITransport transport, byte rack, byte slot)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _rack = rack;
        _slot = slot;
    }

    /// <summary>
    /// Perform the full S7 connection handshake: COTP CR/CC + S7 Communication Setup.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        // Step 1: COTP Connection Request
        var cotpCr = CotpPacket.BuildConnectionRequest(_rack, _slot);
        await SendTpktAsync(cotpCr, ct).ConfigureAwait(false);

        // Step 2: Receive COTP Connection Confirm
        var ccFrame = await ReceiveTpktAsync(ct).ConfigureAwait(false);

        if (!ValidateCotpConfirm(ccFrame))
            throw new IOException("COTP connection rejected by PLC.");

        // Step 3: S7 Communication Setup
        var setupRequest = S7Message.BuildSetupCommunication(
            GetNextPduReference(), maxAmqCalling: 1, maxAmqCalled: 1, pduSize: 480);
        await SendS7Async(setupRequest, ct).ConfigureAwait(false);

        // Step 4: Receive Setup response
        var setupResponse = await ReceiveS7Async(ct).ConfigureAwait(false);
        _negotiatedPduSize = S7Message.ParseSetupPduSize(setupResponse);

        _connected = true;
    }

    /// <summary>
    /// Send an S7 request and receive the response.
    /// </summary>
    public async ValueTask<S7Response> SendAsync(byte[] s7Request, CancellationToken ct = default)
    {
        await SendS7Async(s7Request, ct).ConfigureAwait(false);
        var responseData = await ReceiveS7Async(ct).ConfigureAwait(false);
        return S7Message.ParseResponse(responseData);
    }

    public ushort GetNextPduReference() => ++_pduReference;

    public async ValueTask DisposeAsync()
    {
        _connected = false;
        // No explicit disconnect needed for S7 - just close the TCP connection
    }

    /// <summary>
    /// Send raw data wrapped in TPKT frame.
    /// </summary>
    private async ValueTask SendTpktAsync(byte[] data, CancellationToken ct)
    {
        using var writer = new PacketWriter(data.Length + TpktPacket.HeaderSize);
        TpktPacket.Write(writer, data);
        await _transport.SendAsync(writer.GetWrittenMemory(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Send an S7 message wrapped in COTP DT + TPKT.
    /// </summary>
    private async ValueTask SendS7Async(byte[] s7Data, CancellationToken ct)
    {
        using var cotpWriter = new PacketWriter(s7Data.Length + 8);
        CotpPacket.WriteDtHeader(cotpWriter);
        cotpWriter.WriteBytes(s7Data);
        var cotpBytes = cotpWriter.ToArray();

        using var tpktWriter = new PacketWriter(cotpBytes.Length + TpktPacket.HeaderSize);
        TpktPacket.Write(tpktWriter, cotpBytes);
        await _transport.SendAsync(tpktWriter.GetWrittenMemory(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Receive a TPKT-framed message.
    /// </summary>
    private async ValueTask<byte[]> ReceiveTpktAsync(CancellationToken ct)
    {
        return await _transport.ReceiveFramedAsync(
            TpktPacket.HeaderSize,
            TpktPacket.GetLengthFromHeader,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Receive an S7 response: TPKT -> COTP DT -> S7 data.
    /// </summary>
    private async ValueTask<byte[]> ReceiveS7Async(CancellationToken ct)
    {
        var tpktFrame = await ReceiveTpktAsync(ct).ConfigureAwait(false);
        return ExtractS7Payload(tpktFrame);
    }

    private static bool ValidateCotpConfirm(byte[] tpktFrame)
    {
        var cotpData = TpktPacket.GetPayload(tpktFrame);
        return CotpPacket.ValidateConnectionConfirm(cotpData);
    }

    /// <summary>
    /// Extract S7 payload from a TPKT frame (synchronous, no Span-in-async issue).
    /// </summary>
    private static byte[] ExtractS7Payload(byte[] tpktFrame)
    {
        var cotpData = TpktPacket.GetPayload(tpktFrame);
        var s7Data = CotpPacket.GetDtPayload(cotpData);
        return s7Data.ToArray();
    }
}
