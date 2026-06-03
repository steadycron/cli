namespace SteadyCron.Cli.Output;

/// <summary>
/// A controlled, user-facing error: the message is printed as-is and the process exits with
/// <see cref="ExitCode"/>. Use this instead of letting raw exceptions surface for expected failures.
/// </summary>
public sealed class CliException : Exception
{
    public CliException(string message, int exitCode = ExitCodes.Error)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
