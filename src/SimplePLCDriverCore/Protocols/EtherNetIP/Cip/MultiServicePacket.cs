using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// Builds CIP Multiple Service Packet (service 0x0A) for batching
/// multiple read/write operations into a single network round-trip.
///
/// Request format:
///   Service: 0x0A (Multiple Service Packet)
///   Path Size: 0x02 (words)
///   Path: class 0x02 (Message Router), instance 1
///   Number of Services (2 bytes LE)
///   Offset Table (2 bytes each, offsets from start of service data)
///   Service Data (concatenated CIP requests)
///
/// Response format:
///   Reply Service: 0x8A
///   Reserved: 0
///   General Status
///   Additional Status Size
///   Number of Services (2 bytes LE)
///   Offset Table (2 bytes each)
///   Service Responses (each with its own CIP reply header)
/// </summary>
internal static class MultiServicePacket
{
    // Overhead per service in the offset table
    private const int OffsetTableEntrySize = 2;

    // Fixed overhead: service(1) + path_size(1) + path(4) + count(2)
    private const int FixedOverhead = 8;

    /// <summary>
    /// Build a Multiple Service Packet request from individual CIP requests.
    /// </summary>
    /// <param name="requests">Individual CIP request messages to batch.</param>
    /// <returns>The combined Multiple Service Packet request.</returns>
    public static byte[] Build(IReadOnlyList<byte[]> requests)
    {
        if (requests.Count == 0)
            throw new ArgumentException("At least one request is required", nameof(requests));

        if (requests.Count == 1)
            return requests[0]; // No need to wrap a single request

        // Calculate total size
        var offsetTableSize = requests.Count * OffsetTableEntrySize;
        var serviceDataSize = 0;
        foreach (var req in requests)
            serviceDataSize += req.Length;

        var totalDataSize = 2 + offsetTableSize + serviceDataSize; // count + offsets + data
        var path = CipPath.BuildClassInstancePath(CipClasses.MessageRouter, 1);

        using var writer = new PacketWriter(FixedOverhead + totalDataSize);

        // Service
        writer.WriteUInt8(CipServices.MultipleServicePacket);

        // Path to Message Router
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);

        // Number of services
        writer.WriteUInt16LE((ushort)requests.Count);

        // Offset table - offsets are relative to the start of the "number of services" field
        // First service starts right after the offset table
        var baseOffset = 2 + offsetTableSize; // past count + all offset entries
        var currentOffset = baseOffset;
        foreach (var req in requests)
        {
            writer.WriteUInt16LE((ushort)currentOffset);
            currentOffset += req.Length;
        }

        // Service data - concatenate all requests
        foreach (var req in requests)
            writer.WriteBytes(req);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse a Multiple Service Packet response into individual CIP responses.
    /// </summary>
    /// <param name="responseData">
    /// The response data portion (after CIP reply header has been stripped).
    /// </param>
    /// <returns>Individual CIP response data for each service.</returns>
    public static CipResponse[] ParseResponse(ReadOnlyMemory<byte> responseData)
    {
        var reader = new PacketReader(responseData);

        var serviceCount = reader.ReadUInt16LE();

        // Read offset table
        var offsets = new ushort[serviceCount];
        for (var i = 0; i < serviceCount; i++)
            offsets[i] = reader.ReadUInt16LE();

        // Parse each service response
        var responses = new CipResponse[serviceCount];
        for (var i = 0; i < serviceCount; i++)
        {
            var start = offsets[i];
            var end = (i + 1 < serviceCount)
                ? offsets[i + 1]
                : responseData.Length;

            var serviceData = responseData.Slice(start, end - start);
            responses[i] = CipMessage.ParseResponse(serviceData);
        }

        return responses;
    }

    /// <summary>
    /// Split a list of CIP requests into groups that fit within the connection size limit.
    /// Each group can be sent as a single Multiple Service Packet.
    /// </summary>
    /// <param name="requests">Individual CIP request messages.</param>
    /// <param name="maxConnectionSize">Maximum CIP connection data size (e.g., 4002 for Large Forward Open).</param>
    /// <returns>Groups of request indices, each group fits in one packet.</returns>
    public static List<List<int>> SplitIntoGroups(
        IReadOnlyList<byte[]> requests, int maxConnectionSize = 4002)
    {
        var groups = new List<List<int>>();
        var currentGroup = new List<int>();
        var currentSize = FixedOverhead; // base overhead

        for (var i = 0; i < requests.Count; i++)
        {
            var requestSize = requests[i].Length + OffsetTableEntrySize; // request + offset entry

            if (currentGroup.Count > 0 && currentSize + requestSize > maxConnectionSize)
            {
                // Current group is full, start a new one
                groups.Add(currentGroup);
                currentGroup = new List<int>();
                currentSize = FixedOverhead;
            }

            currentGroup.Add(i);
            currentSize += requestSize;
        }

        if (currentGroup.Count > 0)
            groups.Add(currentGroup);

        return groups;
    }
}
