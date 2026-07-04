using System.ComponentModel;
using Spectre.Console.Cli;

namespace SteadyCron.Cli.Commands.Manifest;

/// <summary>Flags shared by every <c>manifest add &lt;resource&gt;</c> command.</summary>
public abstract class ManifestAddSettingsBase : CommandSettings
{
    [CommandOption("-f|--file <PATH>")]
    [Description("Target manifest file (default: steadycron.yaml in the current directory).")]
    public string? File { get; set; }

    [CommandOption("--dry-run")]
    [Description("Print the generated block without writing to the file.")]
    public bool DryRun { get; set; }

    [CommandOption("--terraform")]
    [Description("Reserved for a future Terraform target — not yet supported for 'add'.")]
    public bool Terraform { get; set; }

    public string TargetPath => string.IsNullOrWhiteSpace(File) ? "steadycron.yaml" : File;
}
