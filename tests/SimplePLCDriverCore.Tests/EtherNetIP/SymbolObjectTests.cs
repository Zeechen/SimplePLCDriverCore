using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class SymbolObjectTests
{
    /// <summary>
    /// Build a fake symbol instance entry as the PLC would return it.
    /// Format: instanceId(4) + nameStatus(2) + nameLen(2) + name(var) +
    ///         typeStatus(2) + type(2) + dimStatus(2) + dim0(4) + dim1(4) + dim2(4) +
    ///         accessStatus(2) + access(2)
    /// </summary>
    private static byte[] BuildSymbolEntry(
        uint instanceId, string name, ushort typeCode,
        uint dim0 = 0, uint dim1 = 0, uint dim2 = 0)
    {
        using var writer = new PacketWriter(128);

        // Instance ID
        writer.WriteUInt32LE(instanceId);

        // Attribute 1: Name
        writer.WriteUInt16LE(0); // status = success
        writer.WriteUInt16LE((ushort)name.Length);
        writer.WriteAscii(name);

        // Attribute 2: Type
        writer.WriteUInt16LE(0); // status = success
        writer.WriteUInt16LE(typeCode);

        // Attribute 7: Dimensions
        writer.WriteUInt16LE(0); // status = success
        writer.WriteUInt32LE(dim0);
        writer.WriteUInt32LE(dim1);
        writer.WriteUInt32LE(dim2);

        // Attribute 8: External Access
        writer.WriteUInt16LE(0); // status = success
        writer.WriteUInt16LE(0x03); // read/write

        return writer.ToArray();
    }

    [Fact]
    public void BuildGetInstanceAttributeListRequest_CreatesValidPacket()
    {
        var request = SymbolObject.BuildGetInstanceAttributeListRequest(0);

        Assert.Equal(CipServices.GetInstanceAttributeList, request[0]);
        // Path should point to Symbol class (0x6B)
        Assert.True(request.Length > 4);
    }

    [Fact]
    public void ParseResponse_SingleScalarTag()
    {
        var entry = BuildSymbolEntry(1, "MyDINT", CipDataTypes.Dint);

        var (lastInstanceId, tags) = SymbolObject.ParseInstanceAttributeListResponse(entry);

        Assert.Equal(1U, lastInstanceId);
        Assert.Single(tags);
        Assert.Equal("MyDINT", tags[0].Name);
        Assert.Equal(CipDataTypes.Dint, tags[0].RawTypeCode);
        Assert.False(tags[0].IsStructure);
        Assert.Empty(tags[0].Dimensions);
    }

    [Fact]
    public void ParseResponse_ArrayTag()
    {
        var entry = BuildSymbolEntry(2, "MyArray", CipDataTypes.Real, dim0: 10);

        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(entry);

        Assert.Single(tags);
        Assert.Equal("MyArray", tags[0].Name);
        Assert.Single(tags[0].Dimensions);
        Assert.Equal(10, tags[0].Dimensions[0]);
    }

    [Fact]
    public void ParseResponse_2DArrayTag()
    {
        var entry = BuildSymbolEntry(3, "Matrix", CipDataTypes.Int, dim0: 5, dim1: 10);

        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(entry);

        Assert.Single(tags);
        Assert.Equal(2, tags[0].Dimensions.Length);
        Assert.Equal(5, tags[0].Dimensions[0]);
        Assert.Equal(10, tags[0].Dimensions[1]);
    }

    [Fact]
    public void ParseResponse_StructureTag()
    {
        // Structure type: bit 15 set, lower bits = template instance ID
        ushort structType = 0x8000 | 0x0123; // template instance 0x123

        var entry = BuildSymbolEntry(4, "MyUDT", structType);

        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(entry);

        Assert.Single(tags);
        Assert.Equal("MyUDT", tags[0].Name);
        Assert.True(tags[0].IsStructure);
        Assert.Equal(0x0123, tags[0].TemplateInstanceId);
        Assert.Equal(PlcDataType.Structure, tags[0].DataType);
    }

    [Fact]
    public void ParseResponse_ProgramScopedTag()
    {
        var entry = BuildSymbolEntry(5, "Program:MainProgram.LocalTag", CipDataTypes.Dint);

        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(entry);

        Assert.Single(tags);
        Assert.True(tags[0].IsProgramScoped);
        Assert.Equal("MainProgram", tags[0].ProgramName);
    }

    [Fact]
    public void ParseResponse_SkipsInternalTags()
    {
        using var writer = new PacketWriter(256);
        writer.WriteBytes(BuildSymbolEntry(1, "__internalTag", CipDataTypes.Dint));
        writer.WriteBytes(BuildSymbolEntry(2, "PublicTag", CipDataTypes.Real));

        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(writer.ToArray());

        Assert.Single(tags);
        Assert.Equal("PublicTag", tags[0].Name);
    }

    [Fact]
    public void ParseResponse_MultipleTags()
    {
        using var writer = new PacketWriter(512);
        writer.WriteBytes(BuildSymbolEntry(1, "Tag1", CipDataTypes.Bool));
        writer.WriteBytes(BuildSymbolEntry(2, "Tag2", CipDataTypes.Real));
        writer.WriteBytes(BuildSymbolEntry(3, "Tag3", CipDataTypes.String));

        var (lastInstanceId, tags) = SymbolObject.ParseInstanceAttributeListResponse(writer.ToArray());

        Assert.Equal(3U, lastInstanceId);
        Assert.Equal(3, tags.Count);
        Assert.Equal("Tag1", tags[0].Name);
        Assert.Equal("Tag2", tags[1].Name);
        Assert.Equal("Tag3", tags[2].Name);
    }

    [Fact]
    public void ParseResponse_EmptyData_ReturnsNoTags()
    {
        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(ReadOnlyMemory<byte>.Empty);

        Assert.Empty(tags);
    }

    [Fact]
    public void ParseResponse_StringTag_HasCorrectType()
    {
        var entry = BuildSymbolEntry(10, "MyString", CipDataTypes.String);

        var (_, tags) = SymbolObject.ParseInstanceAttributeListResponse(entry);

        Assert.Single(tags);
        Assert.Equal(PlcDataType.String, tags[0].DataType);
        Assert.Equal("STRING", tags[0].TypeName);
    }
}
