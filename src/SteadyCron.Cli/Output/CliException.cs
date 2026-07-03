namespace SteadyCron.Cli.Output;

/// <summary>
/// A controlled, user-facing error: the message is printed as-is and the process exits with
/// <see cref="ExitCode"/>. Use this instead of letting raw exceptions surface for expected failures.
/// </summary>
public sealed class CliException : Exception
{
    public CliException(string message, int exitCode = ExitCodes.Error, bool isAuthRequired = false)
        : base(message)
    {
        ExitCode = exitCode;
        IsAuthRequired = isAuthRequired;
    }

    public int ExitCode { get; }

    /// <summary>
    /// True for the specific "no credentials configured" case. Rendered via
    /// <see cref="OutputContext.AuthRequiredMessage"/> (the exact undecorated 3-line block from
    /// SPEC-20b §3.4) instead of the usual <see cref="OutputContext.Error"/> line.
    /// </summary>
    public bool IsAuthRequired { get; }
}
