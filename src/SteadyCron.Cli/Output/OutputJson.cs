using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteadyCron.Cli.Output;

/// <summary>Options for <c>--json</c> output: snake_case, indented, nulls preserved.</summary>
public static class OutputJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
