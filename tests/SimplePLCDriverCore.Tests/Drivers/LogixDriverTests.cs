using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.EtherNetIP;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Drivers;

public class LogixDriverTests
{
    /// <summary>
    /// Build a Forward Open success response wrapped in SendRRData.
    /// </summary>
    private static byte[] BuildForwardOpenResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.LargeForwardOpen | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0); // success
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt32LE(0x11111111); // O->T
        cipWriter.WriteUInt32LE(0x22222222); // T->O
        cipWriter.WriteUInt16LE(1);           // serial
        cipWriter.WriteUInt16LE(1);           // vendor
        cipWriter.WriteUInt32LE(12345);       // originator serial
        cipWriter.WriteUInt32LE(2000000);     // O->T RPI
        cipWriter.WriteUInt32LE(2000000);     // T->O RPI

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build a tag list response (SymbolObject GetInstanceAttributeList).
    /// </summary>
    private static byte[] BuildTagListResponse(uint sessionHandle, bool isPartial = false)
    {
        // Build CIP response with tag entries
        using var cipWriter = new PacketWriter();

        // CIP header
        cipWriter.WriteUInt8(CipServices.GetInstanceAttributeList | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(isPartial ? (byte)0x06 : (byte)0x00); // success or partial
        cipWriter.WriteUInt8(0);

        // Tag entry 1: "TestDint" - DINT scalar
        cipWriter.WriteUInt32LE(1); // instance ID
        cipWriter.WriteUInt16LE(0); // name status
        cipWriter.WriteUInt16LE(8); // name length
        cipWriter.WriteAscii("TestDint");
        cipWriter.WriteUInt16LE(0); // type status
        cipWriter.WriteUInt16LE(CipDataTypes.Dint);
        cipWriter.WriteUInt16LE(0); // dim status
        cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0);
        cipWriter.WriteUInt16LE(0); // access status
        cipWriter.WriteUInt16LE(3); // read/write

        // Tag entry 2: "TestReal" - REAL scalar
        cipWriter.WriteUInt32LE(2);
        cipWriter.WriteUInt16LE(0);
        cipWriter.WriteUInt16LE(8);
        cipWriter.WriteAscii("TestReal");
        cipWriter.WriteUInt16LE(0);
        cipWriter.WriteUInt16LE(CipDataTypes.Real);
        cipWriter.WriteUInt16LE(0);
        cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0);
        cipWriter.WriteUInt16LE(0);
        cipWriter.WriteUInt16LE(3);

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build an empty tag list response (no more tags).
    /// </summary>
    private static byte[] BuildEmptyTagListResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.GetInstanceAttributeList | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8((byte)CipGeneralStatus.PathDestinationUnknown); // no more instances
        cipWriter.WriteUInt8(0);

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build a connected CIP response wrapped in SendUnitData.
    /// </summary>
    private static byte[] BuildSendUnitDataResponse(
        uint sessionHandle, uint connectionId, ushort sequenceNumber, byte[] cipPayload)
    {
        using var cpfWriter = new PacketWriter();
        cpfWriter.WriteUInt32LE(0);          // interface handle
        cpfWriter.WriteUInt16LE(0);          // timeout
        cpfWriter.WriteUInt16LE(2);          // 2 items

        cpfWriter.WriteUInt16LE(0x00A1);
        cpfWriter.WriteUInt16LE(4);
        cpfWriter.WriteUInt32LE(connectionId);

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

    private static (FakeTransport Transport, LogixDriver Driver) CreateTestDriver()
    {
        var transport = new FakeTransport();
        var options = new ConnectionOptions
        {
            KeepAliveInterval = TimeSpan.Zero,
        };
        var driver = new LogixDriver("127.0.0.1", options, () => transport);
        return (transport, driver);
    }

    private static void EnqueueConnectSequence(FakeTransport transport)
    {
        const uint sessionHandle = 0xABCD;

        // 1. RegisterSession
        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(sessionHandle));

        // 2. ForwardOpen
        transport.EnqueueResponse(BuildForwardOpenResponse(sessionHandle));

        // 3. Tag list upload (non-partial = complete, only one response needed)
        transport.EnqueueResponse(BuildTagListResponse(sessionHandle));
    }

    [Fact]
    public async Task Connect_UploadsTagDatabase()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            Assert.True(driver.IsConnected);

            // Tag browser should have the uploaded tags
            var tags = await driver.GetTagsAsync();
            Assert.Equal(2, tags.Count);
            Assert.Equal("TestDint", tags[0].Name);
            Assert.Equal("TestReal", tags[1].Name);
        }
    }

    [Fact]
    public async Task GetPrograms_ReturnsEmptyWhenNoProgramTags()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();
            var programs = await driver.GetProgramsAsync();
            Assert.Empty(programs);
        }
    }

    [Fact]
    public async Task GetAllUdtDefinitions_ReturnsEmptyWhenNoStructs()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();
            var udts = await driver.GetAllUdtDefinitionsAsync();
            Assert.Empty(udts);
        }
    }

    [Fact]
    public async Task ReadAsync_SingleTag()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue a read response
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0); // success
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.Dint);
            cipReply.WriteInt32LE(42);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var result = await driver.ReadAsync("TestDint");

            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Value.AsInt32());
        }
    }

    [Fact]
    public async Task WriteAsync_SingleTag()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue a write response
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.WriteTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0); // success
            cipReply.WriteUInt8(0);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var result = await driver.WriteAsync("TestDint", 99);

            Assert.True(result.IsSuccess);
        }
    }

    [Fact]
    public async Task ReadAsync_Batch()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue two separate read responses (batch of 2 will send individually since
            // the SendBatchAsync for count=1 doesn't wrap, and for count=2 it wraps in MultiService)
            // Since we have 2 tags, SendBatch wraps in MultiServicePacket
            using var batchReply = new PacketWriter();
            batchReply.WriteUInt8(CipServices.MultipleServicePacket | CipServices.ReplyMask);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt8(0); // success
            batchReply.WriteUInt8(0);

            // Build a proper multi-service response
            // Offset table: 2 entries
            batchReply.WriteUInt16LE(2); // service count

            // We need to calculate offsets
            // Offset table: 2 * 2 bytes = 4 bytes
            // Service 1 starts at offset 4+2 = 6 (offset from start of data, after count)
            // Service 1 size: 4 (header) + 2 (type) + 4 (value) = 10 bytes
            // Service 2 starts at 6 + 10 = 16
            batchReply.WriteUInt16LE(6);  // offset to service 1
            batchReply.WriteUInt16LE(16); // offset to service 2

            // Service 1 response: ReadTag reply for DINT=42
            batchReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt16LE(CipDataTypes.Dint);
            batchReply.WriteInt32LE(42);

            // Service 2 response: ReadTag reply for REAL=3.14
            batchReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt8(0);
            batchReply.WriteUInt16LE(CipDataTypes.Real);
            batchReply.WriteSingleLE(3.14f);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, batchReply.ToArray()));

            var results = await driver.ReadAsync(["TestDint", "TestReal"]);

            Assert.Equal(2, results.Length);
            Assert.True(results[0].IsSuccess);
            Assert.True(results[1].IsSuccess);
            Assert.Equal(42, results[0].Value.AsInt32());
            Assert.Equal(3.14f, results[1].Value.AsSingle());
        }
    }

    [Fact]
    public async Task Disconnect_CleansUp()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await driver.ConnectAsync();
        Assert.True(driver.IsConnected);

        await driver.DisconnectAsync();
        Assert.False(driver.IsConnected);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task ReadAsync_NotConnected_Throws()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => driver.ReadAsync("SomeTag").AsTask());
        }
    }

    [Fact]
    public void PlcDriverFactory_CreateLogix_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateLogix("192.168.1.100");
        Assert.NotNull(driver);
        Assert.False(driver.IsConnected);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreateCompactLogix_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateCompactLogix("192.168.1.100");
        Assert.NotNull(driver);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreateControlLogix_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateControlLogix("192.168.1.100", slot: 2);
        Assert.NotNull(driver);
        driver.Dispose();
    }

    // --- STRING Structure Handling ---

    /// <summary>
    /// Build a tag list response that includes a STRING structure tag.
    /// STRING in Logix PLCs has type code 0x8001 (structure bit + template ID 1).
    /// </summary>
    private static byte[] BuildStringTagListResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.GetInstanceAttributeList | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0); // success
        cipWriter.WriteUInt8(0);

        // Tag entry: "TestString" - STRING structure (template ID 1)
        cipWriter.WriteUInt32LE(1); // instance ID
        cipWriter.WriteUInt16LE(0); // name status
        cipWriter.WriteUInt16LE(10); // name length
        cipWriter.WriteAscii("TestString");
        cipWriter.WriteUInt16LE(0); // type status
        cipWriter.WriteUInt16LE(0x8001); // STRING structure type (bit 15 + template ID 1)
        cipWriter.WriteUInt16LE(0); // dim status
        cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0);
        cipWriter.WriteUInt16LE(0); // access status
        cipWriter.WriteUInt16LE(3); // read/write

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build the GetAttributeList response for a STRING template.
    /// </summary>
    private static byte[] BuildStringTemplateAttributesResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.GetAttributeList | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0); // success
        cipWriter.WriteUInt8(0);

        // Attribute response: count, then (id, status, value) for each
        cipWriter.WriteUInt16LE(3); // 3 attributes

        // Attribute 2: Member Count = 2 (LEN + DATA)
        cipWriter.WriteUInt16LE(2); // attr ID
        cipWriter.WriteUInt16LE(0); // success
        cipWriter.WriteUInt16LE(2); // member count

        // Attribute 4: Definition Size = 8 words (32 bytes)
        cipWriter.WriteUInt16LE(4); // attr ID
        cipWriter.WriteUInt16LE(0); // success
        cipWriter.WriteUInt32LE(8); // 8 x 4 = 32 bytes

        // Attribute 5: Structure Byte Size = 88
        cipWriter.WriteUInt16LE(5); // attr ID
        cipWriter.WriteUInt16LE(0); // success
        cipWriter.WriteUInt32LE(88); // standard STRING size

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build the ReadTemplate response with STRING member definitions and name.
    /// </summary>
    private static byte[] BuildStringTemplateDefinitionResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0); // success
        cipWriter.WriteUInt8(0);

        // Member 0 (LEN): Info=0, TypeCode=DINT (0x00C4), Offset=0
        cipWriter.WriteUInt16LE(0);        // info
        cipWriter.WriteUInt16LE(0x00C4);   // DINT
        cipWriter.WriteUInt32LE(0);        // offset

        // Member 1 (DATA): Info=82 (array size), TypeCode=SINT (0x00C2), Offset=4
        cipWriter.WriteUInt16LE(82);       // info (array size = 82)
        cipWriter.WriteUInt16LE(0x00C2);   // SINT
        cipWriter.WriteUInt32LE(4);        // offset

        // Names: "STRING\0LEN\0DATA\0"
        cipWriter.WriteAscii("STRING");
        cipWriter.WriteUInt8(0); // null terminator
        cipWriter.WriteAscii("LEN");
        cipWriter.WriteUInt8(0);
        cipWriter.WriteAscii("DATA");
        cipWriter.WriteUInt8(0);

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    private static void EnqueueConnectSequenceWithStringTag(FakeTransport transport)
    {
        const uint sessionHandle = 0xABCD;

        // 1. RegisterSession
        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(sessionHandle));
        // 2. ForwardOpen
        transport.EnqueueResponse(BuildForwardOpenResponse(sessionHandle));
        // 3. Tag list (with STRING tag)
        transport.EnqueueResponse(BuildStringTagListResponse(sessionHandle));
        // 4. Template attributes for STRING (template ID 1)
        transport.EnqueueResponse(BuildStringTemplateAttributesResponse(sessionHandle));
        // 5. Template definition for STRING
        transport.EnqueueResponse(BuildStringTemplateDefinitionResponse(sessionHandle));
    }

    [Fact]
    public async Task ReadAsync_StringTag_DecodesAsText()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequenceWithStringTag(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Build a read response with STRING structure data.
            // CIP ReadTag for structures uses abbreviated type 0x02A0 + 2-byte handle.
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0); // success
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.AbbreviatedStructureType); // 0x02A0
            cipReply.WriteUInt16LE(0x1234); // structure handle (CRC)

            // STRING data: 4-byte length + ASCII chars + padding to 88 bytes
            var stringData = new byte[88];
            BinaryPrimitives.WriteInt32LittleEndian(stringData, 5); // 5 chars
            stringData[4] = (byte)'H';
            stringData[5] = (byte)'e';
            stringData[6] = (byte)'l';
            stringData[7] = (byte)'l';
            stringData[8] = (byte)'o';
            cipReply.WriteBytes(stringData);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var result = await driver.ReadAsync("TestString");

            Assert.True(result.IsSuccess);
            Assert.Equal("Hello", result.Value.AsString());
            Assert.Equal(PlcDataType.String, result.Value.DataType);
            Assert.Equal("STRING", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_StringTag_EmptyString()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequenceWithStringTag(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.AbbreviatedStructureType); // 0x02A0
            cipReply.WriteUInt16LE(0x1234); // structure handle

            var stringData = new byte[88]; // all zeros = empty string
            cipReply.WriteBytes(stringData);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var result = await driver.ReadAsync("TestString");

            Assert.True(result.IsSuccess);
            Assert.Equal("", result.Value.AsString());
            Assert.Equal("STRING", result.TypeName);
        }
    }

    /// <summary>
    /// Build a tag list response that includes a UDT tag with a STRING member.
    /// Simulates: MyUDT (template ID 2) which has a "Name" member of type STRING (template ID 1).
    /// </summary>
    private static byte[] BuildUdtWithStringTagListResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.GetInstanceAttributeList | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0); // success
        cipWriter.WriteUInt8(0);

        // Tag: "MyUDT" - structure (template ID 2)
        cipWriter.WriteUInt32LE(1); // instance ID
        cipWriter.WriteUInt16LE(0); // name status
        cipWriter.WriteUInt16LE(5); // name length
        cipWriter.WriteAscii("MyUDT");
        cipWriter.WriteUInt16LE(0); // type status
        cipWriter.WriteUInt16LE(0x8002); // structure bit + template ID 2
        cipWriter.WriteUInt16LE(0); // dim status
        cipWriter.WriteUInt32LE(5); cipWriter.WriteUInt32LE(0); cipWriter.WriteUInt32LE(0); // 1D array [5]
        cipWriter.WriteUInt16LE(0); // access status
        cipWriter.WriteUInt16LE(3); // read/write

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build template attributes response for a UDT with 2 members (Value: DINT, Name: STRING).
    /// </summary>
    private static byte[] BuildMyUdtTemplateAttributesResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.GetAttributeList | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt16LE(3);

        cipWriter.WriteUInt16LE(2); cipWriter.WriteUInt16LE(0); cipWriter.WriteUInt16LE(2); // 2 members
        cipWriter.WriteUInt16LE(4); cipWriter.WriteUInt16LE(0); cipWriter.WriteUInt32LE(10); // def size words
        cipWriter.WriteUInt16LE(5); cipWriter.WriteUInt16LE(0); cipWriter.WriteUInt32LE(92); // struct size

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    /// <summary>
    /// Build template definition for MyUDT: Value (DINT @offset 0) + Name (STRING @offset 4).
    /// </summary>
    private static byte[] BuildMyUdtTemplateDefinitionResponse(uint sessionHandle)
    {
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0);
        cipWriter.WriteUInt8(0);

        // Member 0 (Value): Info=0, TypeCode=DINT (0x00C4), Offset=0
        cipWriter.WriteUInt16LE(0);
        cipWriter.WriteUInt16LE(0x00C4);
        cipWriter.WriteUInt32LE(0);

        // Member 1 (Name): Info=0, TypeCode=STRING structure (0x8001), Offset=4
        cipWriter.WriteUInt16LE(0);
        cipWriter.WriteUInt16LE(0x8001); // structure bit + template ID 1 (STRING)
        cipWriter.WriteUInt32LE(4);

        // Names: "MyUDTType\0Value\0Name\0"
        cipWriter.WriteAscii("MyUDTType");
        cipWriter.WriteUInt8(0);
        cipWriter.WriteAscii("Value");
        cipWriter.WriteUInt8(0);
        cipWriter.WriteAscii("Name");
        cipWriter.WriteUInt8(0);

        return MockEipResponse.BuildSendRRDataResponse(sessionHandle, cipWriter.ToArray());
    }

    private static void EnqueueConnectSequenceWithUdtStringMember(FakeTransport transport)
    {
        const uint sessionHandle = 0xABCD;

        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(sessionHandle));
        transport.EnqueueResponse(BuildForwardOpenResponse(sessionHandle));
        // Tag list with MyUDT array tag
        transport.EnqueueResponse(BuildUdtWithStringTagListResponse(sessionHandle));
        // Template attributes for MyUDT (template ID 2)
        transport.EnqueueResponse(BuildMyUdtTemplateAttributesResponse(sessionHandle));
        // Template definition for MyUDT
        transport.EnqueueResponse(BuildMyUdtTemplateDefinitionResponse(sessionHandle));
        // Template attributes for STRING (template ID 1) — recursive upload
        transport.EnqueueResponse(BuildStringTemplateAttributesResponse(sessionHandle));
        // Template definition for STRING
        transport.EnqueueResponse(BuildStringTemplateDefinitionResponse(sessionHandle));
    }

    [Fact]
    public async Task ReadAsync_StringMemberInUdt_DecodesAsText()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequenceWithUdtStringMember(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Build CIP read response with abbreviated structure type (as real PLC does)
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0); // success
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.AbbreviatedStructureType); // 0x02A0
            cipReply.WriteUInt16LE(0x5678); // structure handle (CRC)

            // STRING data: "World"
            var stringData = new byte[88];
            BinaryPrimitives.WriteInt32LittleEndian(stringData, 5);
            stringData[4] = (byte)'W';
            stringData[5] = (byte)'o';
            stringData[6] = (byte)'r';
            stringData[7] = (byte)'l';
            stringData[8] = (byte)'d';
            cipReply.WriteBytes(stringData);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            // Read a STRING member from a UDT array element (like user's TUBEBUNDLE[0].Tubes[0].LocationName)
            var result = await driver.ReadAsync("MyUDT[0].Name");

            Assert.True(result.IsSuccess, $"Read failed: {result.Error}");
            Assert.Equal("World", result.Value.AsString());
            Assert.Equal(PlcDataType.String, result.Value.DataType);
            Assert.Equal("STRING", result.TypeName);
        }
    }

    /// <summary>
    /// When the tag database is empty (tag list upload returned no tags),
    /// STRING detection should still work via data-pattern fallback.
    /// Standard STRING is 88 bytes: 4-byte LEN + 82-byte DATA + 2 padding.
    /// </summary>
    [Fact]
    public async Task ReadAsync_StringTag_EmptyDatabase_DecodesViaPatternFallback()
    {
        var (transport, driver) = CreateTestDriver();
        const uint sessionHandle = 0xABCD;

        // Connect sequence WITHOUT any tags in the tag list
        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(sessionHandle));
        transport.EnqueueResponse(BuildForwardOpenResponse(sessionHandle));
        // Empty tag list (PathDestinationUnknown = no instances)
        transport.EnqueueResponse(BuildEmptyTagListResponse(sessionHandle));

        await using (driver)
        {
            await driver.ConnectAsync();

            // Verify database is empty
            var tags = await driver.GetTagsAsync();
            Assert.Empty(tags);

            // Build a read response with STRING structure data
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0); // success
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt16LE(CipDataTypes.AbbreviatedStructureType); // 0x02A0
            cipReply.WriteUInt16LE(0xAAAA); // structure handle (unknown)

            // STRING data: "Hello" (88 bytes total)
            var stringData = new byte[88];
            BinaryPrimitives.WriteInt32LittleEndian(stringData, 5);
            stringData[4] = (byte)'H';
            stringData[5] = (byte)'e';
            stringData[6] = (byte)'l';
            stringData[7] = (byte)'l';
            stringData[8] = (byte)'o';
            cipReply.WriteBytes(stringData);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                sessionHandle, 0x11111111, 1, cipReply.ToArray()));

            var result = await driver.ReadAsync("UnknownTag");

            Assert.True(result.IsSuccess, $"Read failed: {result.Error}");
            Assert.Equal("Hello", result.Value.AsString());
            Assert.Equal(PlcDataType.String, result.Value.DataType);
            Assert.Equal("STRING", result.TypeName);
        }
    }

    [Fact]
    public async Task WriteAsync_StringTag_EncodesStringValue()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequenceWithStringTag(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue a write success response
            using var cipReply = new PacketWriter();
            cipReply.WriteUInt8(CipServices.WriteTag | CipServices.ReplyMask);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0);
            cipReply.WriteUInt8(0);

            transport.EnqueueResponse(BuildSendUnitDataResponse(
                0xABCD, 0x11111111, 1, cipReply.ToArray()));

            var result = await driver.WriteAsync("TestString", "Hello");

            Assert.True(result.IsSuccess, $"Write failed: {result.Error} [{result.ErrorDetail}]");

            // Verify the sent data contains the encoded string
            // The last sent packet should contain the write request with encoded STRING bytes
            var lastSent = transport.SentData[^1];
            // Find the STRING data in the sent packet (after CIP/EIP headers)
            // The STRING should contain: 4-byte length (5) + "Hello" + padding
            var found = false;
            for (var i = 0; i < lastSent.Length - 8; i++)
            {
                if (BinaryPrimitives.ReadInt32LittleEndian(lastSent.AsSpan(i)) == 5 &&
                    lastSent[i + 4] == (byte)'H' &&
                    lastSent[i + 5] == (byte)'e' &&
                    lastSent[i + 6] == (byte)'l' &&
                    lastSent[i + 7] == (byte)'l' &&
                    lastSent[i + 8] == (byte)'o')
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Encoded STRING data not found in sent packet");
        }
    }
}
