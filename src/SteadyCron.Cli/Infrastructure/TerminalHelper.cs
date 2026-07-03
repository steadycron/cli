namespace SteadyCron.Cli.Infrastructure;

/// <summary>
/// Detects a genuinely interactive terminal — both stdin and stdout attached, not redirected.
/// Used by commands that are fundamentally prompt-driven (<c>signup</c>, <c>login</c>,
/// <c>init</c>) and must fail fast with a clear error when run non-interactively, rather than
/// hanging on a read from a closed/piped stdin.
/// </summary>
public static class TerminalHelper
{
    public static bool IsInteractive() =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected;
}
