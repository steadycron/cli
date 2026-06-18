using System.Text.Json;
using SteadyCron.Cli.Output;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class OutputContextTests
{
    [Fact]
    public void WriteJson_does_not_wrap_long_unbroken_strings()
    {
        // Regression: OutputContext.WriteJson used to route through IAnsiConsole.WriteLine,
        // which renders plain text through Spectre's word-wrap pipeline — a long string with
        // no whitespace (a URL, a token, ...) gets a literal newline inserted at the detected
        // console width, corrupting the JSON. WriteJson must bypass that and write verbatim.
        var longUrl = "https://api.steadycron.com/api/badges/jobs/" + new string('a', 120);

        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var output = new OutputContext(json: true, quiet: false, noColor: true);
            output.WriteJson(new { badge_url = longUrl });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var captured = writer.ToString();
        var parsed = JsonDocument.Parse(captured); // throws if a stray newline broke the JSON
        Assert.Equal(longUrl, parsed.RootElement.GetProperty("badge_url").GetString());
    }
}
