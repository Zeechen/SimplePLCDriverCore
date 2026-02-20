using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// In-memory cache of PLC tag metadata (names, types, dimensions) and UDT definitions.
/// Populated on connection via tag list upload from the PLC.
/// Enables "typeless" tag access by providing type information for read/write operations.
/// </summary>
internal sealed class TagDatabase
{
    // Tag name -> PlcTagInfo (case-insensitive for convenience)
    private readonly Dictionary<string, PlcTagInfo> _tags = new(StringComparer.OrdinalIgnoreCase);

    // Program name -> tag list
    private readonly Dictionary<string, List<PlcTagInfo>> _programTags = new(StringComparer.OrdinalIgnoreCase);

    // Template instance ID -> UDT definition
    private readonly Dictionary<ushort, UdtDefinition> _udtDefinitions = new();

    // UDT name -> UDT definition
    private readonly Dictionary<string, UdtDefinition> _udtByName = new(StringComparer.OrdinalIgnoreCase);

    // Program names
    private readonly List<string> _programs = new();

    /// <summary>All controller-scoped tags.</summary>
    public IReadOnlyList<PlcTagInfo> ControllerTags =>
        _tags.Values.Where(t => !t.IsProgramScoped).ToList();

    /// <summary>All tags including program-scoped.</summary>
    public IReadOnlyList<PlcTagInfo> AllTags => _tags.Values.ToList();

    /// <summary>All program names.</summary>
    public IReadOnlyList<string> Programs => _programs;

    /// <summary>All UDT definitions.</summary>
    public IReadOnlyList<UdtDefinition> UdtDefinitions => _udtDefinitions.Values.ToList();

    /// <summary>Whether the database has been populated.</summary>
    public bool IsPopulated => _tags.Count > 0;

    /// <summary>
    /// Look up a tag by name. Returns null if not found.
    /// Handles program-scoped tags (e.g., "Program:MainProgram.MyTag")
    /// and dotted UDT member paths (e.g., "MyUDT.Field1").
    /// </summary>
    public PlcTagInfo? LookupTag(string tagName)
    {
        // Direct lookup first (most common case)
        if (_tags.TryGetValue(tagName, out var tag))
            return tag;

        // For dotted paths, look up the base tag (before array index and member access)
        var baseName = GetBaseTagName(tagName);
        if (baseName != tagName && _tags.TryGetValue(baseName, out tag))
            return tag;

        return null;
    }

    /// <summary>
    /// Get tags for a specific program.
    /// </summary>
    public IReadOnlyList<PlcTagInfo> GetProgramTags(string programName)
    {
        return _programTags.TryGetValue(programName, out var tags)
            ? tags
            : [];
    }

    /// <summary>
    /// Get a UDT definition by template instance ID.
    /// </summary>
    public UdtDefinition? GetUdtByTemplateId(ushort templateInstanceId)
    {
        return _udtDefinitions.TryGetValue(templateInstanceId, out var def) ? def : null;
    }

    /// <summary>
    /// Get a UDT definition by type name.
    /// </summary>
    public UdtDefinition? GetUdtByName(string typeName)
    {
        return _udtByName.TryGetValue(typeName, out var def) ? def : null;
    }

    /// <summary>
    /// Add a tag to the database.
    /// </summary>
    public void AddTag(PlcTagInfo tagInfo)
    {
        _tags[tagInfo.Name] = tagInfo;

        if (tagInfo.IsProgramScoped && tagInfo.ProgramName != null)
        {
            if (!_programTags.TryGetValue(tagInfo.ProgramName, out var programList))
            {
                programList = new List<PlcTagInfo>();
                _programTags[tagInfo.ProgramName] = programList;
            }
            programList.Add(tagInfo);
        }
    }

    /// <summary>
    /// Add a program name.
    /// </summary>
    public void AddProgram(string programName)
    {
        if (!_programs.Contains(programName, StringComparer.OrdinalIgnoreCase))
            _programs.Add(programName);
    }

    /// <summary>
    /// Add a UDT definition.
    /// </summary>
    public void AddUdtDefinition(UdtDefinition definition)
    {
        _udtDefinitions[definition.TemplateInstanceId] = definition;
        _udtByName[definition.Name] = definition;
    }

    /// <summary>
    /// Check if a UDT definition represents a STRING type.
    /// Matches by name ("STRING") or by structural pattern (LEN: DINT + DATA: SINT array).
    /// </summary>
    public static bool IsStringUdt(UdtDefinition udt)
    {
        if (string.Equals(udt.Name, "STRING", StringComparison.OrdinalIgnoreCase))
            return true;

        // Custom string types follow the same pattern: LEN (DINT) + DATA (SINT array)
        if (udt.Members.Count == 2)
        {
            var hasLen = udt.Members.Any(m =>
                string.Equals(m.Name, "LEN", StringComparison.OrdinalIgnoreCase) &&
                m.DataType == PlcDataType.Dint);
            var hasData = udt.Members.Any(m =>
                string.Equals(m.Name, "DATA", StringComparison.OrdinalIgnoreCase) &&
                m.DataType == PlcDataType.Sint &&
                m.Dimensions.Length > 0);
            return hasLen && hasData;
        }

        return false;
    }

    /// <summary>
    /// Check if a template instance ID represents a STRING type.
    /// </summary>
    public bool IsStringTemplate(ushort templateInstanceId)
    {
        var udt = GetUdtByTemplateId(templateInstanceId);
        return udt != null && IsStringUdt(udt);
    }

    /// <summary>
    /// Clear all cached data.
    /// </summary>
    public void Clear()
    {
        _tags.Clear();
        _programTags.Clear();
        _udtDefinitions.Clear();
        _udtByName.Clear();
        _programs.Clear();
    }

    /// <summary>
    /// Resolve the CIP type code for a tag path, including UDT member paths.
    /// For "MyUDT.IntField", returns the DINT type code instead of the structure type.
    /// For "MyTag", returns the tag's own type code.
    /// Returns 0 if the type cannot be resolved.
    /// </summary>
    public ushort ResolveMemberTypeCode(string tagName)
    {
        // Direct tag lookup - no member access
        if (_tags.TryGetValue(tagName, out var directTag))
            return directTag.RawTypeCode;

        // Try to find the base tag and resolve the member chain
        var baseName = GetBaseTagName(tagName);
        if (baseName == tagName || !_tags.TryGetValue(baseName, out var baseTag))
            return 0;

        if (!baseTag.IsStructure || baseTag.TemplateInstanceId == 0)
            return 0;

        // Extract the member path after the base tag name
        var memberPath = tagName[baseName.Length..];

        // Strip leading array index if present (e.g., "MyArray[0].Field" -> ".Field")
        if (memberPath.StartsWith('['))
        {
            var closeBracket = memberPath.IndexOf(']');
            if (closeBracket < 0)
                return 0;
            memberPath = memberPath[(closeBracket + 1)..];
        }

        // Strip leading dot
        if (!memberPath.StartsWith('.'))
            return 0;
        memberPath = memberPath[1..];

        // Walk the UDT member chain
        return ResolveMemberTypeCodeFromUdt(baseTag.TemplateInstanceId, memberPath);
    }

    private ushort ResolveMemberTypeCodeFromUdt(ushort templateInstanceId, string memberPath)
    {
        var udt = GetUdtByTemplateId(templateInstanceId);
        if (udt == null)
            return 0;

        // Split on first dot or bracket for nested member access
        var dotIndex = memberPath.IndexOf('.');
        var bracketIndex = memberPath.IndexOf('[');

        string currentMember;
        string? remaining = null;

        if (dotIndex >= 0 && (bracketIndex < 0 || dotIndex < bracketIndex))
        {
            currentMember = memberPath[..dotIndex];
            remaining = memberPath[(dotIndex + 1)..];
        }
        else if (bracketIndex >= 0)
        {
            currentMember = memberPath[..bracketIndex];
            var closeBracket = memberPath.IndexOf(']', bracketIndex);
            if (closeBracket >= 0 && closeBracket + 1 < memberPath.Length && memberPath[closeBracket + 1] == '.')
                remaining = memberPath[(closeBracket + 2)..];
        }
        else
        {
            currentMember = memberPath;
        }

        var member = udt.Members.FirstOrDefault(m =>
            string.Equals(m.Name, currentMember, StringComparison.OrdinalIgnoreCase));

        if (member == null)
            return 0;

        // If there's more path to resolve, recurse into nested structure
        if (remaining != null && member.IsStructure && member.TemplateInstanceId > 0)
            return ResolveMemberTypeCodeFromUdt(member.TemplateInstanceId, remaining);

        // Return the member's type code
        if (member.IsStructure)
            return (ushort)(0x8000 | member.TemplateInstanceId);

        return member.DataType switch
        {
            PlcDataType.Bool => CipDataTypes.Bool,
            PlcDataType.Sint => CipDataTypes.Sint,
            PlcDataType.Int => CipDataTypes.Int,
            PlcDataType.Dint => CipDataTypes.Dint,
            PlcDataType.Lint => CipDataTypes.Lint,
            PlcDataType.Usint => CipDataTypes.Usint,
            PlcDataType.Uint => CipDataTypes.Uint,
            PlcDataType.Udint => CipDataTypes.Udint,
            PlcDataType.Ulint => CipDataTypes.Ulint,
            PlcDataType.Real => CipDataTypes.Real,
            PlcDataType.Lreal => CipDataTypes.Lreal,
            PlcDataType.String => CipDataTypes.String,
            _ => 0,
        };
    }

    /// <summary>
    /// Get the base tag name (strip array indices and member access).
    /// "MyArray[5].Field" -> "MyArray"
    /// "Program:Main.Tag" -> "Program:Main.Tag" (program prefix is part of the tag name)
    /// "MyUDT.Member" -> "MyUDT"
    /// </summary>
    private static string GetBaseTagName(string tagName)
    {
        // Handle Program: prefix - keep it as part of the name
        var startIndex = 0;
        if (tagName.StartsWith("Program:", StringComparison.OrdinalIgnoreCase))
        {
            // Find the dot after program name: "Program:MainProgram.Tag"
            var progDot = tagName.IndexOf('.', 8); // after "Program:"
            if (progDot < 0)
                return tagName;
            startIndex = progDot + 1;
        }

        // Find the first '.' or '[' after the program prefix
        var bracketPos = tagName.IndexOf('[', startIndex);
        var dotPos = tagName.IndexOf('.', startIndex);

        int endPos;
        if (bracketPos >= 0 && dotPos >= 0)
            endPos = Math.Min(bracketPos, dotPos);
        else if (bracketPos >= 0)
            endPos = bracketPos;
        else if (dotPos >= 0)
            endPos = dotPos;
        else
            return tagName;

        if (startIndex > 0)
        {
            // Include the program prefix: "Program:Main.BaseTag"
            return tagName[..endPos];
        }

        return tagName[..endPos];
    }
}
