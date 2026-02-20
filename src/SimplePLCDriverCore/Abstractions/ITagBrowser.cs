namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Interface for PLC metadata discovery - tag browsing, programs, UDT definitions.
/// </summary>
public interface ITagBrowser
{
    /// <summary>Get all controller-scoped tags.</summary>
    ValueTask<IReadOnlyList<PlcTagInfo>> GetTagsAsync(CancellationToken ct = default);

    /// <summary>Get tags for a specific program.</summary>
    ValueTask<IReadOnlyList<PlcTagInfo>> GetProgramTagsAsync(
        string programName, CancellationToken ct = default);

    /// <summary>Get all program names.</summary>
    ValueTask<IReadOnlyList<string>> GetProgramsAsync(CancellationToken ct = default);

    /// <summary>Get UDT definition by name.</summary>
    ValueTask<UdtDefinition?> GetUdtDefinitionAsync(
        string typeName, CancellationToken ct = default);

    /// <summary>Get all UDT definitions.</summary>
    ValueTask<IReadOnlyList<UdtDefinition>> GetAllUdtDefinitionsAsync(
        CancellationToken ct = default);
}
