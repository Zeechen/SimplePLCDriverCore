using System.Buffers.Binary;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.EtherNetIP;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Common;

public class ConnectionManagerTests
{
    /// <summary>
    /// Build a mock Forward Open success response wrapped in SendRRData.
    /// </summary>
    private static byte[] BuildForwardOpenResponse(uint sessionHandle)
    {
        // CIP ForwardOpen reply
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.LargeForwardOpen | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0); // reserved
        cipWriter.WriteUInt8(0); // success
        cipWriter.WriteUInt8(0); // no additional status

        // ForwardOpen response data
        cipWriter.WriteUInt32LE(0x11111111); // O->T connection ID
        cipWriter.WriteUInt32LE(0x22222222); // T->O connection ID
        cipWriter.WriteUInt16LE(1);           // connection serial
        cipWriter.WriteUInt16LE(1);           // vendor ID
        cipWriter.WriteUInt32LE(12345);       // originator serial
        cipWriter.WriteUInt32LE(2000000);     // O->T RPI
        cipWriter.WriteUInt32LE(2000000);     // T->O RPI

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build a mock connected CIP response wrapped in SendUnitData.
    /// </summary>
    private static byte[] BuildSendUnitDataResponse(
        uint sessionHandle, uint connectionId, ushort sequenceNumber, byte[] cipPayload)
    {
        using var cpfWriter = new PacketWriter();
        cpfWriter.WriteUInt32LE(0);          // interface handle
        cpfWriter.WriteUInt16LE(0);          // timeout
        cpfWriter.WriteUInt16LE(2);          // 2 items

        // Connected Address
        cpfWriter.WriteUInt16LE(0x00A1);
        cpfWriter.WriteUInt16LE(4);
        cpfWriter.WriteUInt32LE(connectionId);

        // Connected Data (sequence + payload)
        cpfWriter.WriteUInt16LE(0x00B1);
        cpfWriter.WriteUInt16LE((ushort)(cipPayload.Length + 2));
        cpfWriter.WriteUInt16LE(sequenceNumber);
        cpfWriter.WriteBytes(cipPayload);

        var cpfData = cpfWriter.ToArray();

        var message = new byte[24 + cpfData.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(0), 0x0070); // SendUnitData
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(2), (ushort)cpfData.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), sessionHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(8), 0); // success
        cpfData.CopyTo(message, 24);

        return message;
    }

    private static (FakeTransport Transport, ConnectionManager Manager) CreateTestManager()
    {
        var transport = new FakeTransport();
        var options = new ConnectionOptions
        {
            KeepAliveInterval = TimeSpan.Zero, // disable keepalive for tests
        };
        var manager = new ConnectionManager("127.0.0.1", options, () => transport);
        return (transport, manager);
    }

    private static void EnqueueConnectSequence(FakeTransport transport)
    {
        // 1. RegisterSession response
        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(0xABCD));

        // 2. ForwardOpen response
        transport.EnqueueResponse(BuildForwardOpenResponse(0xABCD));
    }

    [Fact]
    public async Task Connect_EstablishesFullConnection()
    {
        var (transport, manager) = CreateTestManager();
        EnqueueConnectSequence(transport);

        await using (manager)
        {
            await manager.ConnectAsync();
            Assert.True(manager.IsConnected);
            Assert.Equal(4002, manager.ConnectionSize);
        }
    }

    [Fact]
    public async Task Send_Connected_ReceivesCipResponse()
    {
        var (transport, manager) = CreateTestManager();
        EnqueueConnectSequence(transport);

        await using (manager)
        {
            await manager.ConnectAsync();

            // Enqueue a CIP ReadTag response
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0); // success
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.Dint);
            cipReply.WriteInt32LE(42);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var fakeRequest = CipMessage.BuildReadTagRequest("TestTag");
            var response = await manager.SendAsync(fakeRequest);

            Assert.True(response.IsSuccess);
            Assert.Equal(CipServices.ReadTag, response.Service);
        }
    }

    [Fact]
    public async Task SendBatch_SingleRequest_NoWrapping()
    {
        var (transport, manager) = CreateTestManager();
        EnqueueConnectSequence(transport);

        await using (manager)
        {
            await manager.ConnectAsync();

            // Single request batch
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.Dint);
            cipReply.WriteInt32LE(99);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var requests = new[] { CipMessage.BuildReadTagRequest("Tag1") };
            var responses = await manager.SendBatchAsync(requests);

            Assert.Single(responses);
            Assert.True(responses[0].IsSuccess);
        }
    }

    [Fact]
    public async Task SendBatch_EmptyRequest_ReturnsEmpty()
    {
        var (transport, manager) = CreateTestManager();
        EnqueueConnectSequence(transport);

        await using (manager)
        {
            await manager.ConnectAsync();

            var responses = await manager.SendBatchAsync(Array.Empty<byte[]>());
            Assert.Empty(responses);
        }
    }

    [Fact]
    public async Task Disconnect_CleansUpResources()
    {
        var (transport, manager) = CreateTestManager();
        EnqueueConnectSequence(transport);

        await manager.ConnectAsync();
        Assert.True(manager.IsConnected);

        await manager.DisconnectAsync();
        Assert.False(manager.IsConnected);

        await manager.DisposeAsync(); // should not throw
    }
}
