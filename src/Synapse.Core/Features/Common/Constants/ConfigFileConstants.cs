using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synapse.Core.Features.Common.Constants;

public static class ConfigFileConstants
{
    public const string FileExtension = ".synapse";
    public const string FileFilter = "Configurations Synapse";
    public const string FilePattern = "*.synapse";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
