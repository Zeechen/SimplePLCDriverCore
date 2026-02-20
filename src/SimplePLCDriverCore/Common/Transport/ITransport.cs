namespace SimplePLCDriverCore.Common.Transport;

/// <summary>
/// Abstraction over the network transport for PLC communication.
/// Enables testability by allowing mock/fake transports in unit tests.
/// </summary>
public interface ITransport : IAsyncDisposable, IDisposable
{
    /// <summary>Whether the transport is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>Connect to the remote endpoint.</summary>
    ValueTask ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnect from the remote endpoint.</summary>
    ValueTask DisconnectAsync(CancellationToken ct = default);

    /// <summary>Send data to the remote endpoint.</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Receive exactly <paramref name="count"/> bytes from the remote endpoint.
    /// Returns a byte array containing the received data.
    /// </summary>
    ValueTask<byte[]> ReceiveAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// Receive data with a protocol-specific framing strategy.
    /// Reads the header first, determines total length, then reads the rest.
    /// </summary>
    /// <param name="headerSize">Number of bytes in the fixed header.</param>
    /// <param name="getLengthFromHeader">
    /// Function that extracts the total message length (including header) from the header bytes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete message including header and payload.</returns>
    ValueTask<byte[]> ReceiveFramedAsync(
        int headerSize,
        Func<byte[], int> getLengthFromHeader,
        CancellationToken ct = default);
}
