using System.Text;

namespace SteadyCron.Cli.Infrastructure;

/// <summary>
/// Appends secret-related entries to <c>.gitignore</c>, idempotently. If <c>init</c>'s exported
/// manifest ever references <c>${SC_…}</c> secrets (or the user later runs
/// <c>export --write-env</c>), a committed <c>secrets.env</c> is a real incident — this closes
/// that gap by default rather than relying on the user to remember it.
/// </summary>
public static class GitignoreGuard
{
    private static readonly string[] RequiredEntries = ["secrets.env", "*.env"];

    /// <summary>
    /// Ensures <paramref name="path"/> ignores env-file secrets, creating the file if it doesn't
    /// exist. Returns true if the file was created or modified; false if every required entry was
    /// already present (nothing written).
    /// </summary>
    public static bool EnsureSecretsIgnored(string path)
    {
        var existingText = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var existingEntries = existingText.Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);

        var missing = RequiredEntries.Where(e => !existingEntries.Contains(e)).ToList();
        if (missing.Count == 0)
        {
            return false;
        }

        var sb = new StringBuilder(existingText);
        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.Append('\n');
        }

        if (sb.Length > 0)
        {
            sb.Append('\n'); // blank line separating from whatever the user already had
        }

        sb.Append("# SteadyCron secrets\n");
        foreach (var entry in missing)
        {
            sb.Append(entry).Append('\n');
        }

        File.WriteAllText(path, sb.ToString());
        return true;
    }
}
