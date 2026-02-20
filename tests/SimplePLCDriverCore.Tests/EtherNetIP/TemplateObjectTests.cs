using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class TemplateObjectTests
{
    [Fact]
    public void BuildGetTemplateAttributesRequest_CreatesValidPacket()
    {
        var request = TemplateObject.BuildGetTemplateAttributesRequest(0x0123);

        Assert.Equal(CipServices.GetAttributeList, request[0]);
        Assert.True(request.Length > 4);
    }

    [Fact]
    public void BuildReadTemplateRequest_CreatesValidPacket()
    {
        var request = TemplateObject.BuildReadTemplateRequest(0x0123, 0, 500);

        Assert.Equal(CipServices.ReadTag, request[0]);
        Assert.True(request.Length > 4);
    }

    [Fact]
    public void ParseTemplateAttributes_ReturnsCorrectValues()
    {
        // Build a GetAttributeList response:
        // attrCount(2) + [attrId(2) + status(2) + value(n)] * 3
        using var writer = new PacketWriter(64);

        writer.WriteUInt16LE(3); // attribute count

        // Attr 2: Member Count (UINT)
        writer.WriteUInt16LE(2); // attr ID
        writer.WriteUInt16LE(0); // status = success
        writer.WriteUInt16LE(3); // 3 members

        // Attr 4: Definition Size (UDINT) in 32-bit words
        writer.WriteUInt16LE(4); // attr ID
        writer.WriteUInt16LE(0); // status
        writer.WriteUInt32LE(20); // 20 words = 80 bytes

        // Attr 5: Structure Byte Size (UDINT)
        writer.WriteUInt16LE(5); // attr ID
        writer.WriteUInt16LE(0); // status
        writer.WriteUInt32LE(96); // 96 bytes

        var data = writer.ToArray();
        var (memberCount, defSizeWords, structByteSize) =
            TemplateObject.ParseTemplateAttributes(data);

        Assert.Equal(3, memberCount);
        Assert.Equal(20U, defSizeWords);
        Assert.Equal(96U, structByteSize);
    }

    [Fact]
    public void ParseTemplateDefinition_SimpleUDT()
    {
        // Build template definition with 2 members:
        //   Member 0: "Field1" - DINT at offset 0
        //   Member 1: "Field2" - REAL at offset 4
        using var writer = new PacketWriter(256);

        // Member definitions (8 bytes each)
        // Member 0: info=0, type=DINT(0xC4), offset=0
        writer.WriteUInt16LE(0);             // info
        writer.WriteUInt16LE(CipDataTypes.Dint); // type
        writer.WriteUInt32LE(0);             // offset

        // Member 1: info=0, type=REAL(0xCA), offset=4
        writer.WriteUInt16LE(0);
        writer.WriteUInt16LE(CipDataTypes.Real);
        writer.WriteUInt32LE(4);

        // Names section: template name + member names (null-terminated)
        var names = "MyUDT\0Field1\0Field2\0";
        writer.WriteAscii(names);

        var data = writer.ToArray();
        var udt = TemplateObject.ParseTemplateDefinition(data, 0x0123, 2, 8);

        Assert.Equal("MyUDT", udt.Name);
        Assert.Equal(8, udt.ByteSize);
        Assert.Equal(0x0123, udt.TemplateInstanceId);
        Assert.Equal(2, udt.Members.Count);

        Assert.Equal("Field1", udt.Members[0].Name);
        Assert.Equal(PlcDataType.Dint, udt.Members[0].DataType);
        Assert.Equal(0, udt.Members[0].Offset);

        Assert.Equal("Field2", udt.Members[1].Name);
        Assert.Equal(PlcDataType.Real, udt.Members[1].DataType);
        Assert.Equal(4, udt.Members[1].Offset);
    }

    [Fact]
    public void ParseTemplateDefinition_StripsNameSuffix()
    {
        // Logix appends ";n;m" format to template names
        using var writer = new PacketWriter(128);

        // One member
        writer.WriteUInt16LE(0);
        writer.WriteUInt16LE(CipDataTypes.Dint);
        writer.WriteUInt32LE(0);

        writer.WriteAscii("MyType;1;2\0Value\0");

        var udt = TemplateObject.ParseTemplateDefinition(writer.ToArray(), 1, 1, 4);

        Assert.Equal("MyType", udt.Name);
    }

    [Fact]
    public void ParseTemplateDefinition_SkipsHiddenMembers()
    {
        using var writer = new PacketWriter(256);

        // 3 members, one is hidden padding
        writer.WriteUInt16LE(0);
        writer.WriteUInt16LE(CipDataTypes.Dint);
        writer.WriteUInt32LE(0);

        writer.WriteUInt16LE(0);
        writer.WriteUInt16LE(CipDataTypes.Dint);
        writer.WriteUInt32LE(4);

        writer.WriteUInt16LE(0);
        writer.WriteUInt16LE(CipDataTypes.Real);
        writer.WriteUInt32LE(8);

        writer.WriteAscii("TestUDT\0Visible1\0ZZZZZZZZZZZZZZZZZ\0Visible2\0");

        var udt = TemplateObject.ParseTemplateDefinition(writer.ToArray(), 1, 3, 12);

        // Hidden member should be filtered out
        Assert.Equal(2, udt.Members.Count);
        Assert.Equal("Visible1", udt.Members[0].Name);
        Assert.Equal("Visible2", udt.Members[1].Name);
    }

    [Fact]
    public void ParseTemplateDefinition_NestedStructureMember()
    {
        using var writer = new PacketWriter(128);

        // Member: nested structure type (bit 15 set, template ID = 0x0456)
        ushort structType = 0x8000 | 0x0456;
        writer.WriteUInt16LE(0);
        writer.WriteUInt16LE(structType);
        writer.WriteUInt32LE(0);

        writer.WriteAscii("ParentUDT\0ChildField\0");

        var udt = TemplateObject.ParseTemplateDefinition(writer.ToArray(), 1, 1, 100);

        Assert.Single(udt.Members);
        Assert.True(udt.Members[0].IsStructure);
        Assert.Equal(0x0456, udt.Members[0].TemplateInstanceId);
    }

    [Fact]
    public void ParseTemplateDefinition_ArrayMember()
    {
        using var writer = new PacketWriter(128);

        // Member with array info = 10 (10-element array)
        writer.WriteUInt16LE(10);              // info = array size
        writer.WriteUInt16LE(CipDataTypes.Dint);
        writer.WriteUInt32LE(0);

        writer.WriteAscii("ArrayUDT\0IntArray\0");

        var udt = TemplateObject.ParseTemplateDefinition(writer.ToArray(), 1, 1, 40);

        Assert.Single(udt.Members);
        Assert.Single(udt.Members[0].Dimensions);
        Assert.Equal(10, udt.Members[0].Dimensions[0]);
    }
}
