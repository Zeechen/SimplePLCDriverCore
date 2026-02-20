using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.EtherNetIP;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

/// <summary>
/// Fake transport for unit testing EipSession without real network I/O.
/// </summary>
internal class FakeTransport : ITransport
{
    private readonly Queue<byte[]> _responsesToReturn = new();
    private readonly List<byte[]> _sentData = new();
    private bool _connected;

    public bool IsConnected => _connected;
    public IReadOnlyList<byte[]> SentData => _sentData;

    public void EnqueueResponse(byte[] response)
    {
        _responsesToReturn.Enqueue(response);
    }

    public ValueTask ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        _sentData.Add(data.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]> ReceiveAsync(int count, CancellationToken ct = default)
    {
        if (_responsesToReturn.Count == 0)
            throw new InvalidOperationException("No response enqueued");

        var response = _responsesToReturn.Dequeue();
        if (response.Length < count)
            throw new InvalidOperationException($"Response too short: {response.Length} < {count}");

        return new ValueTask<byte[]>(response[..count]);
    }

    public ValueTask<byte[]> ReceiveFramedAsync(
        int headerSize,
        Func<byte[], int> getLengthFromHeader,
        CancellationToken ct = default)
    {
        if (_responsesToReturn.Count == 0)
            throw new InvalidOperationException("No response enqueued");

        var fullResponse = _responsesToReturn.Dequeue();
        return new ValueTask<byte[]>(fullResponse);
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _connected = false;
    }
}

/// <summary>
/// Helper to build mock EtherNet/IP responses for testing.
/// </summary>
internal static class MockEipResponse
{
    public static byte[] BuildRegisterSessionResponse(uint sessionHandle)
    {
        var response = new byte[28]; // 24 header + 4 data
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(0), 0x0065); // RegisterSession
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(2), 4);       // data length
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(4), sessionHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(8), 0); // status = success
        // data: protocol version + options
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(26), 0);
        return response;
    }

    public static byte[] BuildSendRRDataResponse(
        uint sessionHandle, byte[] cipResponsePayload)
    {
        // Build the CPF wrapper around the CIP response
        using var cpfWriter = new PacketWriter();
        cpfWriter.WriteUInt32LE(0);          // interface handle
        cpfWriter.WriteUInt16LE(0);          // timeout
        cpfWriter.WriteUInt16LE(2);          // item count

        // Null address
        cpfWriter.WriteUInt16LE(0x0000);
        cpfWriter.WriteUInt16LE(0);

        // Unconnected data
        cpfWriter.WriteUInt16LE(0x00B2);
        cpfWriter.WriteUInt16LE((ushort)cipResponsePayload.Length);
        cpfWriter.WriteBytes(cipResponsePayload);

        var cpfData = cpfWriter.ToArray();

        // Full EIP message
        var message = new byte[24 + cpfData.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(0), 0x006F); // SendRRData
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(2), (ushort)cpfData.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), sessionHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(8), 0); // success
        cpfData.CopyTo(message, 24);

        return message;
    }
}

public class EipSessionTests
{
    [Fact]
    public async Task RegisterSession_SetsSessionHandle()
    {
        var transport = new FakeTransport();
        await transport.ConnectAsync();

        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(0x1234));

        await using var session = new EipSession(transport);
        await session.RegisterSessionAsync();

        Assert.True(session.IsSessionRegistered);
        Assert.Equal(0x1234U, session.SessionHandle);
    }

    [Fact]
    public async Task RegisterSession_ThrowsOnError()
    {
        var transport = new FakeTransport();
        await transport.ConnectAsync();

        // Build a response with error status
        var errorResponse = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(errorResponse.AsSpan(0), 0x0065);
        BinaryPrimitives.WriteUInt16LittleEndian(errorResponse.AsSpan(2), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(errorResponse.AsSpan(4), 0); // handle=0
        BinaryPrimitives.WriteUInt32LittleEndian(errorResponse.AsSpan(8), 1); // status=error
        BinaryPrimitives.WriteUInt16LittleEndian(errorResponse.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(errorResponse.AsSpan(26), 0);

        transport.EnqueueResponse(errorResponse);

        await using var session = new EipSession(transport);
        await Assert.ThrowsAsync<IOException>(() =>
            session.RegisterSessionAsync().AsTask());
    }

    [Fact]
    public async Task UnregisterSession_SendsPacket()
    {
        var transport = new FakeTransport();
        await transport.ConnectAsync();

        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(0xABCD));

        await using var session = new EipSession(transport);
        await session.RegisterSessionAsync();
        await session.UnregisterSessionAsync();

        Assert.False(session.IsSessionRegistered);
        Assert.Equal(2, transport.SentData.Count); // register + unregister

        // Verify unregister packet
        var unregPacket = transport.SentData[1];
        Assert.Equal(0x66, unregPacket[0]); // UnregisterSession command
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(unregPacket.AsSpan(4));
        Assert.Equal(0xABCDU, handle);
    }

    [Fact]
    public async Task SendUnconnected_ParsesCipResponse()
    {
        var transport = new FakeTransport();
        await transport.ConnectAsync();

        // Register session
        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(0x5678));

        await using var session = new EipSession(transport);
        await session.RegisterSessionAsync();

        // Build a CIP success response
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(0xCC); // service reply (0x4C | 0x80)
        cipWriter.WriteUInt8(0);    // reserved
        cipWriter.WriteUInt8(0);    // success
        cipWriter.WriteUInt8(0);    // no additional status
        cipWriter.WriteUInt16LE(0x00C4); // DINT type code
        cipWriter.WriteInt32LE(99);       // value

        transport.EnqueueResponse(MockEipResponse.BuildSendRRDataResponse(
            0x5678, cipWriter.ToArray()));

        var fakeCipRequest = new byte[] { 0x4C, 0x00 };
        var response = await session.SendUnconnectedAsync(fakeCipRequest);

        Assert.True(response.IsSuccess);
        Assert.Equal(0x4C, response.Service);
    }

    [Fact]
    public async Task ForwardOpen_ThrowsIfNotRegistered()
    {
        var transport = new FakeTransport();
        await transport.ConnectAsync();

        await using var session = new EipSession(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ForwardOpenAsync().AsTask());
    }

    [Fact]
    public async Task SendConnected_ThrowsIfNotConnected()
    {
        var transport = new FakeTransport();
        await transport.ConnectAsync();

        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(0x1111));

        await using var session = new EipSession(transport);
        await session.RegisterSessionAsync();

        // CIP connection not established, should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendConnectedAsync(new byte[] { 0x4C }).AsTask());
    }
}
