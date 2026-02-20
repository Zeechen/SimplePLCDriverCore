using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class MultiServicePacketTests
{
    [Fact]
    public void Build_SingleRequest_ReturnsUnwrapped()
    {
        var request = new byte[] { 0x4C, 0x02, 0x91, 0x03 }; // fake read tag
        var result = MultiServicePacket.Build(new[] { request });

        // Single request should be returned as-is (no wrapping)
        Assert.Equal(request, result);
    }

    [Fact]
    public void Build_MultipleRequests_WrapsCorrectly()
    {
        var req1 = new byte[] { 0x4C, 0x01, 0x02, 0x03 }; // 4 bytes
        var req2 = new byte[] { 0x4D, 0x01, 0x02 };         // 3 bytes

        var result = MultiServicePacket.Build(new[] { req1, req2 });

        // Service = MultipleServicePacket (0x0A)
        Assert.Equal(CipServices.MultipleServicePacket, result[0]);

        // Path size in words
        var pathSize = result[1];
        Assert.True(pathSize > 0);

        // Path: Message Router class 0x02, instance 1
        // Skip to after the path
        var pathEnd = 2 + pathSize * 2;

        // Number of services = 2
        var serviceCount = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(pathEnd));
        Assert.Equal(2, serviceCount);

        // Offset table (2 entries)
        var offset1 = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(pathEnd + 2));
        var offset2 = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(pathEnd + 4));

        // First offset should be right after count + offset table
        Assert.Equal(2 + 2 * 2, offset1); // count(2) + 2 offsets(4) = 6
        Assert.Equal(2 + 2 * 2 + req1.Length, offset2); // 6 + req1 length

        // Verify embedded request data
        var dataStart = pathEnd;
        Assert.Equal(0x4C, result[dataStart + offset1]); // req1 service
        Assert.Equal(0x4D, result[dataStart + offset2]); // req2 service
    }

    [Fact]
    public void Build_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            MultiServicePacket.Build(Array.Empty<byte[]>()));
    }

    [Fact]
    public void SplitIntoGroups_SingleGroup_WhenFits()
    {
        var requests = new[]
        {
            new byte[100],
            new byte[100],
            new byte[100],
        };

        var groups = MultiServicePacket.SplitIntoGroups(requests, maxConnectionSize: 4002);

        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
        Assert.Equal(new[] { 0, 1, 2 }, groups[0]);
    }

    [Fact]
    public void SplitIntoGroups_MultipleGroups_WhenExceedsSize()
    {
        var requests = new[]
        {
            new byte[200],
            new byte[200],
            new byte[200],
        };

        // Very small max to force splitting
        var groups = MultiServicePacket.SplitIntoGroups(requests, maxConnectionSize: 250);

        Assert.True(groups.Count > 1);

        // All indices should be present
        var allIndices = groups.SelectMany(g => g).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 0, 1, 2 }, allIndices);
    }

    [Fact]
    public void SplitIntoGroups_OneRequestPerGroup_WhenVerySmallLimit()
    {
        var requests = new[]
        {
            new byte[100],
            new byte[100],
        };

        // Limit so small only one request fits per group
        var groups = MultiServicePacket.SplitIntoGroups(requests, maxConnectionSize: 120);

        Assert.Equal(2, groups.Count);
        Assert.Single(groups[0]);
        Assert.Single(groups[1]);
    }

    [Fact]
    public void ParseResponse_ParsesMultipleResponses()
    {
        // Build a fake Multiple Service Packet response
        // Each embedded response is a mini CIP reply

        // Response 1: ReadTag success, DINT=42
        using var resp1Writer = new PacketWriter();
        resp1Writer.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
        resp1Writer.WriteUInt8(0);
        resp1Writer.WriteUInt8(0x00); // success
        resp1Writer.WriteUInt8(0);    // no additional status
        resp1Writer.WriteUInt16LE(CipDataTypes.Dint);
        resp1Writer.WriteInt32LE(42);
        var resp1Bytes = resp1Writer.ToArray();

        // Response 2: ReadTag error
        using var resp2Writer = new PacketWriter();
        resp2Writer.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
        resp2Writer.WriteUInt8(0);
        resp2Writer.WriteUInt8(0x05); // PathDestinationUnknown
        resp2Writer.WriteUInt8(0);
        var resp2Bytes = resp2Writer.ToArray();

        // Build the multi-service response data (just the inner portion)
        using var responseWriter = new PacketWriter();
        var offsetTableSize = 2 * 2; // 2 entries
        var offset1 = 2 + offsetTableSize;
        var offset2 = offset1 + resp1Bytes.Length;

        responseWriter.WriteUInt16LE(2); // service count
        responseWriter.WriteUInt16LE((ushort)offset1);
        responseWriter.WriteUInt16LE((ushort)offset2);
        responseWriter.WriteBytes(resp1Bytes);
        responseWriter.WriteBytes(resp2Bytes);

        var responses = MultiServicePacket.ParseResponse(responseWriter.ToArray());

        Assert.Equal(2, responses.Length);

        // Response 1: success
        Assert.True(responses[0].IsSuccess);
        Assert.Equal(CipServices.ReadTag, responses[0].Service);

        // Response 2: error
        Assert.False(responses[1].IsSuccess);
        Assert.Equal(CipGeneralStatus.PathDestinationUnknown, responses[1].GeneralStatus);
    }
}
