using System.ComponentModel;
using Spectre.Console.Cli;

namespace SteadyCron.Cli.Commands.Jobs;

/// <summary>Settings for commands acting on a single job identified by id or name.</summary>
public class JobTargetSettings : CliSettings
{
    [CommandArgument(0, "<JOB>")]
    [Description("Job id (GUID) or exact job name.")]
    public string Identifier { get; set; } = string.Empty;

    public override Spectre.Console.ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Identifier)
            ? Spectre.Console.ValidationResult.Error("A job id or name is required.")
            : Spectre.Console.ValidationResult.Success();
}
