// =============================================================================
// TagBrowsing Example - SimplePLCDriverCore
// =============================================================================
// Demonstrates the metadata discovery capabilities of the library.
// On connect, the driver uploads the full tag database from the PLC.
// You can browse tags, programs, and UDT definitions programmatically.
// =============================================================================

using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Drivers;

await using var plc = PlcDriverFactory.CreateLogix("192.168.1.100");
await plc.ConnectAsync();
Console.WriteLine("Connected to PLC\n");

// Cast to ITagBrowser for metadata access
// (LogixDriver implements both IPlcDriver and ITagBrowser)
var browser = (ITagBrowser)plc;

// =============================================================================
// List All Controller-Scoped Tags
// =============================================================================

Console.WriteLine("=== Controller Tags ===\n");

var tags = await browser.GetTagsAsync();
Console.WriteLine($"Found {tags.Count} controller-scoped tags:\n");

foreach (var tag in tags)
{
    var dims = tag.Dimensions.Length > 0
        ? $"[{string.Join(",", tag.Dimensions)}]"
        : "";

    Console.WriteLine($"  {tag.Name,-30} {tag.TypeName,-12} {dims}");
}

// =============================================================================
// Filter Tags by Type
// =============================================================================

Console.WriteLine("\n=== Filtered by Type ===\n");

// Find all DINT tags
var dintTags = tags.Where(t => t.DataType == PlcDataType.Dint).ToList();
Console.WriteLine($"DINT tags ({dintTags.Count}):");
foreach (var tag in dintTags.Take(10))
    Console.WriteLine($"  {tag.Name}");
if (dintTags.Count > 10)
    Console.WriteLine($"  ... and {dintTags.Count - 10} more");

// Find all REAL tags
var realTags = tags.Where(t => t.DataType == PlcDataType.Real).ToList();
Console.WriteLine($"\nREAL tags ({realTags.Count}):");
foreach (var tag in realTags.Take(10))
    Console.WriteLine($"  {tag.Name}");

// Find all array tags
var arrayTags = tags.Where(t => t.Dimensions.Length > 0).ToList();
Console.WriteLine($"\nArray tags ({arrayTags.Count}):");
foreach (var tag in arrayTags.Take(10))
{
    var dims = $"[{string.Join(",", tag.Dimensions)}]";
    Console.WriteLine($"  {tag.Name}{dims} ({tag.TypeName})");
}

// Find all structure/UDT tags
var structTags = tags.Where(t => t.IsStructure).ToList();
Console.WriteLine($"\nStructure/UDT tags ({structTags.Count}):");
foreach (var tag in structTags.Take(10))
    Console.WriteLine($"  {tag.Name} ({tag.TypeName})");

// =============================================================================
// List Programs and Program-Scoped Tags
// =============================================================================

Console.WriteLine("\n=== Programs ===\n");

var programs = await browser.GetProgramsAsync();
Console.WriteLine($"Found {programs.Count} programs:\n");

foreach (var program in programs)
{
    Console.WriteLine($"Program: {program}");

    var programTags = await browser.GetProgramTagsAsync(program);
    Console.WriteLine($"  Tags: {programTags.Count}");

    foreach (var tag in programTags.Take(5))
    {
        Console.WriteLine($"    {tag.Name}: {tag.TypeName}");
    }

    if (programTags.Count > 5)
        Console.WriteLine($"    ... and {programTags.Count - 5} more");

    Console.WriteLine();
}

// =============================================================================
// Browse UDT (User Defined Type) Definitions
// =============================================================================

Console.WriteLine("=== UDT Definitions ===\n");

var udts = await browser.GetAllUdtDefinitionsAsync();
Console.WriteLine($"Found {udts.Count} UDT definitions:\n");

foreach (var udt in udts)
{
    Console.WriteLine($"UDT: {udt.Name} ({udt.ByteSize} bytes, ID: 0x{udt.TemplateInstanceId:X4})");
    Console.WriteLine($"  Members ({udt.Members.Count}):");

    foreach (var member in udt.Members)
    {
        var memberDims = member.Dimensions.Length > 0
            ? $"[{string.Join(",", member.Dimensions)}]"
            : "";
        var extra = member.IsStructure ? " (nested struct)" : "";

        Console.WriteLine($"    {member.Name,-20} {member.TypeName,-10} offset={member.Offset,-4} size={member.Size}{memberDims}{extra}");
    }

    Console.WriteLine();
}

// =============================================================================
// Look Up a Specific UDT Definition
// =============================================================================

Console.WriteLine("=== UDT Lookup ===\n");

var myUdt = await browser.GetUdtDefinitionAsync("MyCustomUDT");
if (myUdt != null)
{
    Console.WriteLine($"Found UDT: {myUdt.Name}");
    Console.WriteLine($"Size: {myUdt.ByteSize} bytes");
    Console.WriteLine($"Members:");
    foreach (var member in myUdt.Members)
        Console.WriteLine($"  {member.Name}: {member.TypeName} @ byte {member.Offset}");
}
else
{
    Console.WriteLine("UDT 'MyCustomUDT' not found in PLC");
}

// =============================================================================
// Tag Statistics Summary
// =============================================================================

Console.WriteLine("\n=== Summary ===\n");

Console.WriteLine($"Total controller tags: {tags.Count}");
Console.WriteLine($"Total programs:        {programs.Count}");
Console.WriteLine($"Total UDT definitions: {udts.Count}");
Console.WriteLine($"Scalar tags:           {tags.Count(t => t.Dimensions.Length == 0 && !t.IsStructure)}");
Console.WriteLine($"Array tags:            {tags.Count(t => t.Dimensions.Length > 0)}");
Console.WriteLine($"Structure tags:        {tags.Count(t => t.IsStructure)}");
Console.WriteLine($"Program-scoped tags:   {tags.Count(t => t.IsProgramScoped)}");

Console.WriteLine("\nDone!");
