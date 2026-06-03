using System.ComponentModel;
using Spectre.Console.Cli;

namespace SteadyCron.Cli.Commands;

/// <summary>Global options shared by every command (credentials, output formatting).</summary>
public class CliSettings : CommandSettings
{
    [CommandOption("--api-key <KEY>")]
    [Description("SteadyCron API key (sc_…). Overrides $STEADYCRON_API_KEY and the config file.")]
    public string? ApiKey { get; set; }

    [CommandOption("--api-url <URL>")]
    [Description("API base URL. Overrides $STEADYCRON_API_URL and the config file. Default: https://api.steadycron.com")]
    public string? ApiUrl { get; set; }

    [CommandOption("--json")]
    [Description("Emit machine-readable JSON on stdout instead of formatted text.")]
    public bool Json { get; set; }

    [CommandOption("--no-color")]
    [Description("Disable ANSI colors in output.")]
    public bool NoColor { get; set; }

    [CommandOption("-q|--quiet")]
    [Description("Suppress non-essential output.")]
    public bool Quiet { get; set; }
}
