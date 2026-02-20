using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimplePLCDriverCore.TypeSystem.Json;

internal static class PlcJsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
