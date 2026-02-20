using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace SimplePLCDriverCore.Common.Transport;

/// <summary>
/// High-performance async TCP transport using System.IO.Pipelines.
/// Provides zero-copy buffer management and efficient framing for PLC protocols.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _connectTimeout;

    private Socket? _socket;
    private NetworkStream? _stream;
    private PipeReader? _pipeReader;
    private PipeWriter? _pipeWriter;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);

    public bool IsConnected => _socket?.Connected == true;

    public TcpTransport(string host, int port, TimeSpan? connectTimeout = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true, // Disable Nagle's algorithm for low-latency PLC communication
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            await _socket.ConnectAsync(_host, _port, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Connection to {_host}:{_port} timed out after {_connectTimeout.TotalSeconds}s");
        }

        _stream = new NetworkStream(_socket, ownsSocket: false);

        // Create pipes for high-performance async I/O
        var pipeOptions = new StreamPipeReaderOptions(
            pool: MemoryPool<byte>.Shared,
            bufferSize: 4096,
            minimumReadSize: 1024,
            leaveOpen: true);

        _pipeReader = PipeReader.Create(_stream, pipeOptions);
        _pipeWriter = PipeWriter.Create(_stream, new StreamPipeWriterOptions(
            pool: MemoryPool<byte>.Shared,
            minimumBufferSize: 512,
            leaveOpen: true));
    }

    public async ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        if (_pipeReader != null)
        {
            await _pipeReader.CompleteAsync().ConfigureAwait(false);
            _pipeReader = null;
        }

        if (_pipeWriter != null)
        {
            await _pipeWriter.CompleteAsync().ConfigureAwait(false);
            _pipeWriter = null;
        }

        if (_stream != null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        if (_socket != null)
        {
            try
            {
                if (_socket.Connected)
                    _socket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException) { /* already disconnected */ }
            _socket.Dispose();
            _socket = null;
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var writer = _pipeWriter ?? throw new InvalidOperationException("Transport is not connected");

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await writer.WriteAsync(data, ct).ConfigureAwait(false);
            if (result.IsCanceled)
                throw new OperationCanceledException();
            if (result.IsCompleted)
                throw new IOException("Transport connection closed during send");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask<byte[]> ReceiveAsync(int count, CancellationToken ct = default)
    {
        var reader = _pipeReader ?? throw new InvalidOperationException("Transport is not connected");

        await _receiveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadExactBytesAsync(reader, count, ct).ConfigureAwait(false);
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    public async ValueTask<byte[]> ReceiveFramedAsync(
        int headerSize,
        Func<byte[], int> getLengthFromHeader,
        CancellationToken ct = default)
    {
        var reader = _pipeReader ?? throw new InvalidOperationException("Transport is not connected");

        await _receiveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Step 1: Read the header to determine total message length
            var header = await ReadExactBytesAsync(reader, headerSize, ct).ConfigureAwait(false);
            var totalLength = getLengthFromHeader(header);

            if (totalLength < headerSize)
                throw new InvalidDataException(
                    $"Protocol framing error: total length {totalLength} is less than header size {headerSize}");

            if (totalLength == headerSize)
                return header;

            // Step 2: Read the remaining payload
            var payloadLength = totalLength - headerSize;
            var payload = await ReadExactBytesAsync(reader, payloadLength, ct).ConfigureAwait(false);

            // Step 3: Combine header + payload
            var result = new byte[totalLength];
            header.CopyTo(result, 0);
            payload.CopyTo(result, headerSize);
            return result;
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    /// <summary>
    /// Read exactly N bytes from the PipeReader, handling partial reads.
    /// </summary>
    private static async ValueTask<byte[]> ReadExactBytesAsync(
        PipeReader reader, int count, CancellationToken ct)
    {
        var result = new byte[count];
        var totalRead = 0;

        while (totalRead < count)
        {
            var readResult = await reader.ReadAsync(ct).ConfigureAwait(false);
            var buffer = readResult.Buffer;

            if (buffer.IsEmpty && readResult.IsCompleted)
                throw new IOException(
                    $"Connection closed: received {totalRead} of {count} expected bytes");

            var bytesToCopy = (int)Math.Min(buffer.Length, count - totalRead);

            buffer.Slice(0, bytesToCopy).CopyTo(result.AsSpan(totalRead));
            totalRead += bytesToCopy;

            reader.AdvanceTo(buffer.GetPosition(bytesToCopy));
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _receiveLock.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
