using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SimplePLCDriverCore.Common.Buffers;

/// <summary>
/// High-performance binary packet writer using pooled buffers.
/// Supports both little-endian (EtherNet/IP) and big-endian (S7, FINS, Modbus) protocols.
/// </summary>
public sealed class PacketWriter : IDisposable
{
    private byte[] _buffer;
    private int _position;
    private bool _disposed;

    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    public PacketWriter(int initialCapacity = 512)
    {
        _buffer = Pool.Rent(initialCapacity);
        _position = 0;
    }

    /// <summary>Current write position (number of bytes written).</summary>
    public int Length => _position;

    /// <summary>Get the written bytes as a ReadOnlyMemory.</summary>
    public ReadOnlyMemory<byte> GetWrittenMemory() => _buffer.AsMemory(0, _position);

    /// <summary>Get the written bytes as a ReadOnlySpan.</summary>
    public ReadOnlySpan<byte> GetWrittenSpan() => _buffer.AsSpan(0, _position);

    /// <summary>Copy written data to a new byte array.</summary>
    public byte[] ToArray() => _buffer.AsSpan(0, _position).ToArray();

    /// <summary>Reset writer to beginning without releasing buffer.</summary>
    public void Reset() => _position = 0;

    /// <summary>Get/set the write position for patching values.</summary>
    public int Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    // --- Little-Endian Writers (EtherNet/IP, CIP) ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt8(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt8(sbyte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16LE(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16LE(short value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32LE(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32LE(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64LE(ulong value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64LE(long value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingleLE(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDoubleLE(double value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    // --- Big-Endian Writers (S7, FINS, Modbus) ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16BE(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16BE(short value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteInt16BigEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32BE(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32BE(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64BE(ulong value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteUInt64BigEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingleBE(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleBigEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDoubleBE(double value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteDoubleBigEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    // --- Raw Bytes ---

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    public void WriteBytes(byte[] data) => WriteBytes(data.AsSpan());

    /// <summary>Write a fixed number of zero bytes.</summary>
    public void WriteZeros(int count)
    {
        EnsureCapacity(count);
        _buffer.AsSpan(_position, count).Clear();
        _position += count;
    }

    // --- String Writers ---

    /// <summary>Write ASCII string bytes (no length prefix, no null terminator).</summary>
    public void WriteAscii(string value)
    {
        var byteCount = Encoding.ASCII.GetByteCount(value);
        EnsureCapacity(byteCount);
        Encoding.ASCII.GetBytes(value, _buffer.AsSpan(_position));
        _position += byteCount;
    }

    /// <summary>Write ASCII string padded to even length (CIP symbolic segment requirement).</summary>
    public void WriteAsciiPadded(string value)
    {
        WriteAscii(value);
        if (value.Length % 2 != 0)
            WriteUInt8(0); // pad to even length
    }

    // --- Patching (write at specific offset without advancing position) ---

    /// <summary>Write a uint16 LE at a specific offset (for patching length fields).</summary>
    public void PatchUInt16LE(int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(offset), value);
    }

    /// <summary>Write a uint32 LE at a specific offset.</summary>
    public void PatchUInt32LE(int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset), value);
    }

    // --- Buffer Management ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int additionalBytes)
    {
        if (_position + additionalBytes <= _buffer.Length)
            return;
        Grow(additionalBytes);
    }

    private void Grow(int additionalBytes)
    {
        var newSize = Math.Max(_buffer.Length * 2, _position + additionalBytes);
        var newBuffer = Pool.Rent(newSize);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        Pool.Return(_buffer);
        _buffer = newBuffer;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Pool.Return(_buffer);
            _disposed = true;
        }
    }
}
