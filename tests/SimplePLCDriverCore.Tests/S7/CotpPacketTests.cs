using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.S7;

namespace SimplePLCDriverCore.Tests.S7;

public class CotpPacketTests
{
    [Fact]
    public void BuildConnectionRequest_Rack0Slot0()
    {
        var cr = CotpPacket.BuildConnectionRequest(0, 0);

        Assert.True(cr.Length > 7);
        Assert.Equal(cr.Length - 1, cr[0]); // length indicator
        Assert.Equal(CotpPacket.PduTypeCR, cr[1]); // PDU type
    }

    [Fact]
    public void BuildConnectionRequest_Rack0Slot2()
    {
        var cr = CotpPacket.BuildConnectionRequest(0, 2);

        // Find called TSAP (0xC2 parameter)
        int idx = FindParam(cr, 0xC2);
        Assert.True(idx >= 0, "Called TSAP parameter not found");

        var calledTsap = (ushort)((cr[idx + 2] << 8) | cr[idx + 3]);
        Assert.Equal(0x0102, calledTsap); // 0x0100 | (0*0x20 + 2)
    }

    [Fact]
    public void BuildConnectionRequest_Rack1Slot3()
    {
        var cr = CotpPacket.BuildConnectionRequest(1, 3);

        int idx = FindParam(cr, 0xC2);
        Assert.True(idx >= 0);

        var calledTsap = (ushort)((cr[idx + 2] << 8) | cr[idx + 3]);
        Assert.Equal(0x0123, calledTsap); // 0x0100 | (1*0x20 + 3)
    }

    [Fact]
    public void ValidateConnectionConfirm_ValidCC_ReturnsTrue()
    {
        var cc = new byte[] { 0x06, CotpPacket.PduTypeCC, 0x00, 0x01, 0x00, 0x02, 0x00 };
        Assert.True(CotpPacket.ValidateConnectionConfirm(cc));
    }

    [Fact]
    public void ValidateConnectionConfirm_WrongType_ReturnsFalse()
    {
        var cr = new byte[] { 0x06, CotpPacket.PduTypeCR, 0x00, 0x01, 0x00, 0x02, 0x00 };
        Assert.False(CotpPacket.ValidateConnectionConfirm(cr));
    }

    [Fact]
    public void ValidateConnectionConfirm_TooShort_ReturnsFalse()
    {
        Assert.False(CotpPacket.ValidateConnectionConfirm(new byte[] { 0x01 }));
    }

    [Fact]
    public void WriteDtHeader_ProducesCorrectBytes()
    {
        using var writer = new PacketWriter(8);
        CotpPacket.WriteDtHeader(writer);
        var data = writer.ToArray();

        Assert.Equal(3, data.Length);
        Assert.Equal(0x02, data[0]); // length indicator
        Assert.Equal(CotpPacket.PduTypeDT, data[1]); // DT type
        Assert.Equal(0x80, data[2]); // EOT flag
    }

    [Fact]
    public void GetDtPayload_ExtractsPayload()
    {
        // DT frame: length=2, type=DT, EOT=0x80, then payload
        var frame = new byte[] { 0x02, 0xF0, 0x80, 0xAA, 0xBB, 0xCC };
        var payload = CotpPacket.GetDtPayload(frame);

        Assert.Equal(3, payload.Length);
        Assert.Equal(0xAA, payload[0]);
        Assert.Equal(0xBB, payload[1]);
        Assert.Equal(0xCC, payload[2]);
    }

    [Fact]
    public void GetDtPayload_WrongType_Throws()
    {
        var frame = new byte[] { 0x02, 0xD0, 0x00, 0xAA };
        Assert.Throws<InvalidOperationException>(() => CotpPacket.GetDtPayload(frame));
    }

    [Fact]
    public void GetDtPayload_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CotpPacket.GetDtPayload(new byte[] { 0x02 }));
    }

    [Fact]
    public void GetPduType_ReturnsCorrectType()
    {
        Assert.Equal(CotpPacket.PduTypeDT, CotpPacket.GetPduType(new byte[] { 0x02, 0xF0, 0x80 }));
        Assert.Equal(CotpPacket.PduTypeCC, CotpPacket.GetPduType(new byte[] { 0x06, 0xD0 }));
        Assert.Equal(CotpPacket.PduTypeCR, CotpPacket.GetPduType(new byte[] { 0x06, 0xE0 }));
    }

    [Fact]
    public void GetPduType_TooShort_ReturnsZero()
    {
        Assert.Equal(0, CotpPacket.GetPduType(new byte[] { 0x01 }));
    }

    private static int FindParam(byte[] data, byte paramCode)
    {
        // Skip fixed header (7 bytes: length + type + destRef(2) + srcRef(2) + class)
        for (int i = 7; i < data.Length - 1; i++)
        {
            if (data[i] == paramCode)
                return i;
        }
        return -1;
    }
}
