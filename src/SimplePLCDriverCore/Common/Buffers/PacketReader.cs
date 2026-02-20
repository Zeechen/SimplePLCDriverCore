using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SimplePLCDriverCore.Common.Buffers;

/// <summary>
/// High-performance binary packet reader that operates on ReadOnlyMemory/ReadOnlySpan.
/// Supports both little-endian (EtherNet/IP) and big-endian (S7, FINS, Modbus) protocols.
/// Zero-allocation - reads directly from the source buffer.
/// </summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public PacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public PacketReader(ReadOnlyMemory<byte> buffer) : this(buffer.Span) { }

    public PacketReader(byte[] buffer) : this(buffer.AsSpan()) { }

    /// <summary>Current read position.</summary>
    public int Position
    {
        readonly get => _position;
        set => _position = value;
    }

    /// <summary>Total buffer length.</summary>
    public readonly int Length => _buffer.Length;

    /// <summary>Number of bytes remaining.</summary>
    public readonly int Remaining => _buffer.Length - _position;

    /// <summary>Whether there are more bytes to read.</summary>
    public readonly bool HasRemaining => _position < _buffer.Length;

    /// <summary>Get a slice of the remaining buffer.</summary>
    public readonly ReadOnlySpan<byte> RemainingSpan => _buffer[_position..];

    /// <summary>Get a slice of the buffer from the current position.</summary>
    public readonly ReadOnlySpan<byte> Slice(int length) => _buffer.Slice(_position, length);

    /// <summary>Skip forward by N bytes.</summary>
    public void Skip(int count) => _position += count;

    // --- Little-Endian Readers (EtherNet/IP, CIP) ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadUInt8()
    {
        return _buffer[_position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadInt8()
    {
        return (sbyte)_buffer[_position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16LE()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16LE()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32LE()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32LE()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64LE()
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[_position..]);
        _position += 8;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64LE()
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer[_position..]);
        _position += 8;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingleLE()
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDoubleLE()
    {
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer[_position..]);
        _position += 8;
        return value;
    }

    // --- Big-Endian Readers (S7, FINS, Modbus) ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16BE()
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(_buffer[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16BE()
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(_buffer[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32BE()
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32BE()
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64BE()
    {
        var value = BinaryPrimitives.ReadUInt64BigEndian(_buffer[_position..]);
        _position += 8;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingleBE()
    {
        var value = BinaryPrimitives.ReadSingleBigEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDoubleBE()
    {
        var value = BinaryPrimitives.ReadDoubleBigEndian(_buffer[_position..]);
        _position += 8;
        return value;
    }

    // --- Raw Bytes ---

    /// <summary>Read exactly N bytes.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var span = _buffer.Slice(_position, count);
        _position += count;
        return span;
    }

    /// <summary>Read exactly N bytes into a new byte array.</summary>
    public byte[] ReadBytesToArray(int count)
    {
        var result = _buffer.Slice(_position, count).ToArray();
        _position += count;
        return result;
    }

    // --- String Readers ---

    /// <summary>Read ASCII string of known length.</summary>
    public string ReadAscii(int length)
    {
        var str = Encoding.ASCII.GetString(_buffer.Slice(_position, length));
        _position += length;
        return str;
    }

    /// <summary>Read ASCII string, trimming null terminators.</summary>
    public string ReadAsciiTrimmed(int length)
    {
        var str = ReadAscii(length);
        return str.TrimEnd('\0');
    }

    // --- Peek (read without advancing position) ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly byte PeekUInt8() => _buffer[_position];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ushort PeekUInt16LE() =>
        BinaryPrimitives.ReadUInt16LittleEndian(_buffer[_position..]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint PeekUInt32LE() =>
        BinaryPrimitives.ReadUInt32LittleEndian(_buffer[_position..]);

    // --- Read at specific offset (without moving position) ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly byte ReadUInt8At(int offset) => _buffer[offset];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ushort ReadUInt16LEAt(int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(_buffer[offset..]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint ReadUInt32LEAt(int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(_buffer[offset..]);
}
