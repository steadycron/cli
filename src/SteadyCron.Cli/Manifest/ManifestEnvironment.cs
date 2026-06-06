namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Builds the <c>${...}</c> resolution lookup for manifest loading from optional <c>.env</c> files
/// layered over the process environment, and enforces the env-file guardrail.
///
/// Precedence: an explicit <c>--env-file</c> value wins over the process environment, so a file
/// prepared for a target account is authoritative even if a stale value is exported in the shell
/// (important when restoring into a different account).
/// </summary>
public static class ManifestEnvironment
{
    /// <summary>
    /// Returns a <c>getVar</c> delegate for <see cref="ManifestLoader.LoadFromPaths"/>.
    /// When <paramref name="enforceEnvFile"/> is set and the manifest references any required
    /// placeholder (a <c>${NAME}</c> without a default) while no <c>--env-file</c> was supplied and
    /// <paramref name="allowProcessEnv"/> is false, throws <see cref="ManifestException"/> (exit 2).
    /// </summary>
    public static Func<string, string?> Build(
        IEnumerable<string> manifestPaths,
        IReadOnlyList<string> envFilePaths,
        bool allowProcessEnv,
        bool enforceEnvFile)
    {
        var fileVars = EnvFile.Load(envFilePaths);

        if (enforceEnvFile && envFilePaths.Count == 0 && !allowProcessEnv)
        {
            var required = ScanRequiredPlaceholders(manifestPaths);
            if (required.Count > 0)
            {
                var list = string.Join(", ", required.Select(n => $"${{{n}}}"));
                throw new ManifestException(
                    $"This manifest references {required.Count} secret placeholder(s) that must be supplied: {list}. " +
                    "Pass --env-file <path> with their values, or --allow-process-env to source them from the " +
                    "current environment.");
            }
        }

        return name =>
            fileVars.TryGetValue(name, out var value)
                ? value
                : Environment.GetEnvironmentVariable(name);
    }

    private static IReadOnlyList<string> ScanRequiredPlaceholders(IEnumerable<string> manifestPaths)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in ManifestLoader.ExpandPaths(manifestPaths))
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                // The loader will surface the real read/not-found error in its own pass.
                continue;
            }

            foreach (var name in EnvInterpolator.FindRequiredPlaceholders(text))
            {
                names.Add(name);
            }
        }

        return [.. names];
    }
}
