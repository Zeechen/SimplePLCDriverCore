using System.Buffers.Binary;
using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Tests.Modbus;

public class ModbusAdvancedMessageTests
{
    // ==========================================================================
    // BuildDiagnostics (FC 0x08)
    // ==========================================================================

    [Fact]
    public void BuildDiagnostics_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 1, 0x0000, 0x1234);
        Assert.Equal(12, req.Length);
    }

    [Fact]
    public void BuildDiagnostics_HasCorrectTransactionId()
    {
        var req = ModbusMessage.BuildDiagnostics(0x0042, 1, 0x0000, 0x0000);
        var txId = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(0));
        Assert.Equal(0x0042, txId);
    }

    [Fact]
    public void BuildDiagnostics_HasProtocolIdZero()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 1, 0x0000, 0x0000);
        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(2));
        Assert.Equal(0, protocolId);
    }

    [Fact]
    public void BuildDiagnostics_HasCorrectMbapLength()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 1, 0x0000, 0x0000);
        var length = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4));
        // pduLength set to 5 in the code
        Assert.Equal(5, length);
    }

    [Fact]
    public void BuildDiagnostics_HasCorrectUnitId()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 7, 0x0000, 0x0000);
        Assert.Equal(7, req[6]);
    }

    [Fact]
    public void BuildDiagnostics_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 1, 0x0000, 0x0000);
        Assert.Equal(0x08, req[7]);
    }

    [Fact]
    public void BuildDiagnostics_HasCorrectSubFunction()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 1, 0x0004, 0x0000);
        var subFunction = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(8));
        Assert.Equal(0x0004, subFunction);
    }

    [Fact]
    public void BuildDiagnostics_HasCorrectData()
    {
        var req = ModbusMessage.BuildDiagnostics(1, 1, 0x0000, 0xABCD);
        var data = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(0xABCD, data);
    }

    // ==========================================================================
    // BuildMaskWriteRegister (FC 0x16)
    // ==========================================================================

    [Fact]
    public void BuildMaskWriteRegister_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildMaskWriteRegister(1, 1, 0x0004, 0x00F2, 0x0025);
        Assert.Equal(14, req.Length);
    }

    [Fact]
    public void BuildMaskWriteRegister_HasCorrectMbapLength()
    {
        var req = ModbusMessage.BuildMaskWriteRegister(1, 1, 0x0004, 0x00F2, 0x0025);
        var length = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4));
        Assert.Equal(7, length);
    }

    [Fact]
    public void BuildMaskWriteRegister_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildMaskWriteRegister(1, 1, 0x0004, 0x00F2, 0x0025);
        Assert.Equal(0x16, req[7]);
    }

    [Fact]
    public void BuildMaskWriteRegister_HasCorrectAddress()
    {
        var req = ModbusMessage.BuildMaskWriteRegister(1, 1, 0x0004, 0x00F2, 0x0025);
        var address = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(8));
        Assert.Equal(0x0004, address);
    }

    [Fact]
    public void BuildMaskWriteRegister_HasCorrectAndMask()
    {
        var req = ModbusMessage.BuildMaskWriteRegister(1, 1, 0x0004, 0x00F2, 0x0025);
        var andMask = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(0x00F2, andMask);
    }

    [Fact]
    public void BuildMaskWriteRegister_HasCorrectOrMask()
    {
        var req = ModbusMessage.BuildMaskWriteRegister(1, 1, 0x0004, 0x00F2, 0x0025);
        var orMask = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(12));
        Assert.Equal(0x0025, orMask);
    }

    // ==========================================================================
    // BuildReadWriteMultipleRegisters (FC 0x17)
    // ==========================================================================

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 5, 0x0010, [0x00FF, 0x00AA]);
        // 6 (MBAP) + 1 (unit) + 1 (fc) + 2 (readAddr) + 2 (readQty)
        // + 2 (writeAddr) + 2 (writeQty) + 1 (byteCount) + 4 (data) = 21
        Assert.Equal(21, req.Length);
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 5, 0x0010, [0x00FF]);
        Assert.Equal(0x17, req[7]);
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectReadAddress()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0003, 5, 0x0010, [0x00FF]);
        var readAddr = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(8));
        Assert.Equal(0x0003, readAddr);
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectReadQuantity()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 6, 0x0010, [0x00FF]);
        var readQty = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(6, readQty);
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectWriteAddress()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 5, 0x0010, [0x00FF]);
        var writeAddr = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(12));
        Assert.Equal(0x0010, writeAddr);
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectWriteQuantity()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 5, 0x0010, [0x00FF, 0x00AA]);
        var writeQty = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(14));
        Assert.Equal(2, writeQty);
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectByteCount()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 5, 0x0010, [0x00FF, 0x00AA]);
        Assert.Equal(4, req[16]); // 2 registers * 2 bytes = 4
    }

    [Fact]
    public void BuildReadWriteMultipleRegisters_HasCorrectWriteData()
    {
        var req = ModbusMessage.BuildReadWriteMultipleRegisters(
            1, 1, 0x0000, 5, 0x0010, [0x1234, 0x5678]);
        var val1 = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(17));
        var val2 = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(19));
        Assert.Equal(0x1234, val1);
        Assert.Equal(0x5678, val2);
    }

    // ==========================================================================
    // BuildReadFifoQueue (FC 0x18)
    // ==========================================================================

    [Fact]
    public void BuildReadFifoQueue_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildReadFifoQueue(1, 1, 0x04DE);
        Assert.Equal(10, req.Length);
    }

    [Fact]
    public void BuildReadFifoQueue_HasCorrectMbapLength()
    {
        var req = ModbusMessage.BuildReadFifoQueue(1, 1, 0x04DE);
        var length = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4));
        Assert.Equal(3, length);
    }

    [Fact]
    public void BuildReadFifoQueue_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildReadFifoQueue(1, 1, 0x04DE);
        Assert.Equal(0x18, req[7]);
    }

    [Fact]
    public void BuildReadFifoQueue_HasCorrectPointerAddress()
    {
        var req = ModbusMessage.BuildReadFifoQueue(1, 1, 0x04DE);
        var ptrAddr = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(8));
        Assert.Equal(0x04DE, ptrAddr);
    }

    // ==========================================================================
    // BuildReadDeviceIdentification (FC 0x2B)
    // ==========================================================================

    [Fact]
    public void BuildReadDeviceIdentification_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildReadDeviceIdentification(1, 1, 0x01, 0x00);
        // 6 (MBAP) + 1 (unit) + 1 (fc) + 1 (MEI) + 1 (readCode) + 1 (objectId) = 11
        Assert.Equal(11, req.Length);
    }

    [Fact]
    public void BuildReadDeviceIdentification_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildReadDeviceIdentification(1, 1, 0x01, 0x00);
        Assert.Equal(0x2B, req[7]);
    }

    [Fact]
    public void BuildReadDeviceIdentification_HasMeiType0x0E()
    {
        var req = ModbusMessage.BuildReadDeviceIdentification(1, 1, 0x01, 0x00);
        Assert.Equal(0x0E, req[8]);
    }

    [Fact]
    public void BuildReadDeviceIdentification_HasCorrectReadDeviceIdCode()
    {
        var req = ModbusMessage.BuildReadDeviceIdentification(1, 1, 0x04, 0x00);
        Assert.Equal(0x04, req[9]);
    }

    [Fact]
    public void BuildReadDeviceIdentification_HasCorrectObjectId()
    {
        var req = ModbusMessage.BuildReadDeviceIdentification(1, 1, 0x01, 0x02);
        Assert.Equal(0x02, req[10]);
    }

    // ==========================================================================
    // BuildReadFileRecord (FC 0x14)
    // ==========================================================================

    [Fact]
    public void BuildReadFileRecord_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        // 6 (MBAP) + 1 (unit) + 1 (fc) + 1 (byteCount) + 1 (refType)
        // + 2 (file#) + 2 (record#) + 2 (recordLen) = 16
        Assert.Equal(16, req.Length);
    }

    [Fact]
    public void BuildReadFileRecord_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        Assert.Equal(0x14, req[7]);
    }

    [Fact]
    public void BuildReadFileRecord_HasCorrectByteCount()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        Assert.Equal(7, req[8]); // sub-request size = 7
    }

    [Fact]
    public void BuildReadFileRecord_HasReferenceType0x06()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        Assert.Equal(0x06, req[9]);
    }

    [Fact]
    public void BuildReadFileRecord_HasCorrectFileNumber()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        var fileNum = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(0x0004, fileNum);
    }

    [Fact]
    public void BuildReadFileRecord_HasCorrectRecordNumber()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        var recordNum = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(12));
        Assert.Equal(0x0001, recordNum);
    }

    [Fact]
    public void BuildReadFileRecord_HasCorrectRecordLength()
    {
        var req = ModbusMessage.BuildReadFileRecord(1, 1, 0x0004, 0x0001, 0x0002);
        var recordLen = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(14));
        Assert.Equal(0x0002, recordLen);
    }

    // ==========================================================================
    // BuildWriteFileRecord (FC 0x15)
    // ==========================================================================

    [Fact]
    public void BuildWriteFileRecord_HasCorrectTotalLength()
    {
        byte[] recordData = [0x00, 0x0A, 0x01, 0x02];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        // 6 (MBAP) + 1 (unit) + 1 (fc) + 1 (byteCount) + 1 (refType)
        // + 2 (file#) + 2 (record#) + 2 (recordLen) + 4 (data) = 20
        Assert.Equal(20, req.Length);
    }

    [Fact]
    public void BuildWriteFileRecord_HasCorrectFunctionCode()
    {
        byte[] recordData = [0x00, 0x0A];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        Assert.Equal(0x15, req[7]);
    }

    [Fact]
    public void BuildWriteFileRecord_HasCorrectByteCount()
    {
        byte[] recordData = [0x00, 0x0A, 0x01, 0x02];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        // sub-request size = 7 + 4 = 11
        Assert.Equal(11, req[8]);
    }

    [Fact]
    public void BuildWriteFileRecord_HasReferenceType0x06()
    {
        byte[] recordData = [0x00, 0x0A];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        Assert.Equal(0x06, req[9]);
    }

    [Fact]
    public void BuildWriteFileRecord_HasCorrectFileNumber()
    {
        byte[] recordData = [0x00, 0x0A];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        var fileNum = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(0x0004, fileNum);
    }

    [Fact]
    public void BuildWriteFileRecord_HasCorrectRecordNumber()
    {
        byte[] recordData = [0x00, 0x0A];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        var recordNum = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(12));
        Assert.Equal(0x0007, recordNum);
    }

    [Fact]
    public void BuildWriteFileRecord_HasCorrectRecordLength()
    {
        byte[] recordData = [0x00, 0x0A, 0x01, 0x02];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        // recordLength = 4 / 2 = 2
        var recordLen = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(14));
        Assert.Equal(2, recordLen);
    }

    [Fact]
    public void BuildWriteFileRecord_HasCorrectRecordData()
    {
        byte[] recordData = [0xDE, 0xAD, 0xBE, 0xEF];
        var req = ModbusMessage.BuildWriteFileRecord(1, 1, 0x0004, 0x0007, recordData);
        // Data starts at byte 16 (after MBAP 6 + unit 1 + fc 1 + byteCount 1
        // + refType 1 + file# 2 + record# 2 + recordLen 2 = 16)
        Assert.Equal(0xDE, req[16]);
        Assert.Equal(0xAD, req[17]);
        Assert.Equal(0xBE, req[18]);
        Assert.Equal(0xEF, req[19]);
    }

    // ==========================================================================
    // BuildRaw (any FC)
    // ==========================================================================

    [Fact]
    public void BuildRaw_EmptyPayload_HasCorrectTotalLength()
    {
        var req = ModbusMessage.BuildRaw(1, 1, 0x41, ReadOnlySpan<byte>.Empty);
        // 6 (MBAP) + 1 (unit) + 1 (fc) = 8
        Assert.Equal(8, req.Length);
    }

    [Fact]
    public void BuildRaw_EmptyPayload_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildRaw(1, 1, 0x41, ReadOnlySpan<byte>.Empty);
        Assert.Equal(0x41, req[7]);
    }

    [Fact]
    public void BuildRaw_WithPayload_HasCorrectTotalLength()
    {
        byte[] payload = [0x01, 0x02, 0x03];
        var req = ModbusMessage.BuildRaw(1, 1, 0x41, payload);
        // 6 (MBAP) + 1 (unit) + 1 (fc) + 3 (payload) = 11
        Assert.Equal(11, req.Length);
    }

    [Fact]
    public void BuildRaw_WithPayload_HasCorrectFunctionCode()
    {
        byte[] payload = [0x01, 0x02, 0x03];
        var req = ModbusMessage.BuildRaw(1, 1, 0x41, payload);
        Assert.Equal(0x41, req[7]);
    }

    [Fact]
    public void BuildRaw_WithPayload_HasCorrectPayloadBytes()
    {
        byte[] payload = [0xAA, 0xBB, 0xCC];
        var req = ModbusMessage.BuildRaw(1, 1, 0x41, payload);
        Assert.Equal(0xAA, req[8]);
        Assert.Equal(0xBB, req[9]);
        Assert.Equal(0xCC, req[10]);
    }

    [Fact]
    public void BuildRaw_HasCorrectUnitId()
    {
        var req = ModbusMessage.BuildRaw(1, 0xFF, 0x41, ReadOnlySpan<byte>.Empty);
        Assert.Equal(0xFF, req[6]);
    }

    [Fact]
    public void BuildRaw_HasCorrectTransactionId()
    {
        byte[] payload = [0x01];
        var req = ModbusMessage.BuildRaw(0x1234, 1, 0x41, payload);
        var txId = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(0));
        Assert.Equal(0x1234, txId);
    }
}
