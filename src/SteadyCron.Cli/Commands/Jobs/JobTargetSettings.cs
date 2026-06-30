using System.ComponentModel;
using Spectre.Console.Cli;

namespace SteadyCron.Cli.Commands.Jobs;

/// <summary>Settings for commands acting on a single job identified by job key, name, or id.</summary>
public class JobTargetSettings : CliSettings
{
    [CommandArgument(0, "<JOB>")]
    [Description("Job key, exact name, or id (GUID).")]
    public string Identifier { get; set; } = string.Empty;

    public override Spectre.Console.ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Identifier)
            ? Spectre.Console.ValidationResult.Error("A job key, name, or id is required.")
            : Spectre.Console.ValidationResult.Success();
}
