using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteadyCron.Cli.Api;

/// <summary>
/// JSON options that mirror the SteadyCron API exactly: <c>snake_case</c> property names and
/// <c>snake_case</c> string enums (the API configures
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/> for both properties and enum values).
/// </summary>
public static class SteadyCronJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            // Omit nulls so create/patch payloads stay minimal and the server applies its own
            // defaults for anything the manifest did not specify.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
