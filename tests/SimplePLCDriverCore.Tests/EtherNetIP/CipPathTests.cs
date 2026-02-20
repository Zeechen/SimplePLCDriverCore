using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class CipPathTests
{
    [Fact]
    public void EncodeSymbolicPath_SimpleTagName()
    {
        // "MyTag" (5 chars, odd -> needs pad)
        var path = CipPath.EncodeSymbolicPath("MyTag");

        Assert.Equal(0x91, path[0]);   // symbolic segment type
        Assert.Equal(5, path[1]);      // name length
        Assert.Equal((byte)'M', path[2]);
        Assert.Equal((byte)'y', path[3]);
        Assert.Equal((byte)'T', path[4]);
        Assert.Equal((byte)'a', path[5]);
        Assert.Equal((byte)'g', path[6]);
        Assert.Equal(0x00, path[7]);   // pad byte (odd length)
        Assert.Equal(8, path.Length);
    }

    [Fact]
    public void EncodeSymbolicPath_EvenLengthName()
    {
        // "AB" (2 chars, even -> no pad)
        var path = CipPath.EncodeSymbolicPath("AB");

        Assert.Equal(0x91, path[0]);
        Assert.Equal(2, path[1]);
        Assert.Equal((byte)'A', path[2]);
        Assert.Equal((byte)'B', path[3]);
        Assert.Equal(4, path.Length);  // no pad needed
    }

    [Fact]
    public void EncodeSymbolicPath_WithArrayIndex()
    {
        // "MyArray[3]"
        var path = CipPath.EncodeSymbolicPath("MyArray[3]");

        // Symbolic segment for "MyArray" (7 chars, odd -> pad)
        Assert.Equal(0x91, path[0]);
        Assert.Equal(7, path[1]);
        // ... name bytes ...
        Assert.Equal(0x00, path[9]); // pad

        // Element segment for index 3 (8-bit)
        Assert.Equal(0x28, path[10]); // element segment 8-bit
        Assert.Equal(3, path[11]);    // index
    }

    [Fact]
    public void EncodeSymbolicPath_WithLargeArrayIndex()
    {
        // "Arr[300]" - index > 255, needs 16-bit encoding
        var path = CipPath.EncodeSymbolicPath("Arr[300]");

        // Symbolic segment for "Arr" (3 chars, odd -> pad)
        Assert.Equal(0x91, path[0]);
        Assert.Equal(3, path[1]);
        Assert.Equal(0x00, path[5]); // pad

        // Element segment for index 300 (16-bit)
        Assert.Equal(0x29, path[6]); // element segment 16-bit
        Assert.Equal(0x00, path[7]); // pad
        // 300 = 0x012C in LE
        Assert.Equal(0x2C, path[8]);
        Assert.Equal(0x01, path[9]);
    }

    [Fact]
    public void EncodeSymbolicPath_WithDotMember()
    {
        // "MyUDT.Field"
        var path = CipPath.EncodeSymbolicPath("MyUDT.Field");

        // First segment: "MyUDT" (5 chars, odd -> pad)
        Assert.Equal(0x91, path[0]);
        Assert.Equal(5, path[1]);

        // Second segment: "Field" (5 chars, odd -> pad) starts at offset 8
        Assert.Equal(0x91, path[8]);
        Assert.Equal(5, path[9]);
    }

    [Fact]
    public void EncodeSymbolicPath_WithBitAccess()
    {
        // "MyDINT.5" - bit 5 access
        var path = CipPath.EncodeSymbolicPath("MyDINT.5");

        // First segment: "MyDINT" (6 chars, even -> no pad)
        Assert.Equal(0x91, path[0]);
        Assert.Equal(6, path[1]);

        // Bit access encoded as member segment at offset 8
        Assert.Equal(0x28, path[8]); // member segment 8-bit
        Assert.Equal(5, path[9]);    // bit 5
    }

    [Fact]
    public void EncodeSymbolicPath_ComplexPath()
    {
        // "Program:Main.MyUDT[0].Value"
        // Split: ["Program:Main", "MyUDT[0]", "Value"]
        var path = CipPath.EncodeSymbolicPath("Program:Main.MyUDT[0].Value");

        // First segment: "Program:Main" (12 chars, even)
        Assert.Equal(0x91, path[0]);
        Assert.Equal(12, path[1]);

        // Should parse without errors
        Assert.True(path.Length > 20);
    }

    [Fact]
    public void EncodeClassSegment_8Bit()
    {
        var path = CipPath.BuildClassPath(0x6B); // Symbol Object

        Assert.Equal(0x20, path[0]); // class segment 8-bit
        Assert.Equal(0x6B, path[1]); // class 0x6B
        Assert.Equal(2, path.Length);
    }

    [Fact]
    public void EncodeClassInstancePath_8Bit()
    {
        var path = CipPath.BuildClassInstancePath(0x6B, 1);

        Assert.Equal(0x20, path[0]); // class 8-bit
        Assert.Equal(0x6B, path[1]); // class = Symbol Object
        Assert.Equal(0x24, path[2]); // instance 8-bit
        Assert.Equal(0x01, path[3]); // instance 1
        Assert.Equal(4, path.Length);
    }

    [Fact]
    public void EncodeClassInstancePath_16BitInstance()
    {
        var path = CipPath.BuildClassInstancePath(0x6C, 500);

        Assert.Equal(0x20, path[0]); // class 8-bit
        Assert.Equal(0x6C, path[1]); // class = Template Object
        Assert.Equal(0x25, path[2]); // instance 16-bit
        Assert.Equal(0x00, path[3]); // pad
        // 500 = 0x01F4 LE
        Assert.Equal(0xF4, path[4]);
        Assert.Equal(0x01, path[5]);
    }

    [Fact]
    public void EncodeRoutePath_BackplaneSlot()
    {
        var routePath = CipPath.EncodeRoutePath(2);

        Assert.Equal(2, routePath.Length);
        Assert.Equal(0x01, routePath[0]); // port 1 (backplane)
        Assert.Equal(0x02, routePath[1]); // slot 2
    }

    [Fact]
    public void GetPathSizeInWords_CalculatesCorrectly()
    {
        Assert.Equal(1, CipPath.GetPathSizeInWords(2));
        Assert.Equal(2, CipPath.GetPathSizeInWords(4));
        Assert.Equal(2, CipPath.GetPathSizeInWords(3)); // rounds up
        Assert.Equal(4, CipPath.GetPathSizeInWords(8));
    }
}
