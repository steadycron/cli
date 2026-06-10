using System.ComponentModel;
using Spectre.Console.Cli;

namespace SteadyCron.Cli.Commands.Import;

/// <summary>Options shared by all <c>import</c> subcommands.</summary>
public abstract class ImportBaseSettings : CommandSettings
{
    [CommandOption("-o|--output <FILE>")]
    [Description("Write the manifest to this file instead of stdout.")]
    public string? Output { get; set; }

    [CommandOption("-n|--namespace <NAME>")]
    [Description("Set the 'namespace' field in the emitted manifest.")]
    public string? Namespace { get; set; }

    [CommandOption("--dry-run")]
    [Description("Parse and report counts/warnings without writing any output.")]
    public bool DryRun { get; set; }
}
