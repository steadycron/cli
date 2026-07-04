namespace SteadyCron.Cli.Infrastructure;

/// <summary>
/// Lightweight, dependency-free git repo detection: reads <c>.git</c> directly rather than
/// shelling out to a <c>git</c> binary, since the CLI doesn't assume git is installed. Checks the
/// given directory only (no upward search) — matches every other <c>init</c>-generated file,
/// which is CWD-relative and assumes it's run from the repo root.
/// </summary>
public static class GitRepositoryHelper
{
    public static bool IsGitRepo(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));

    /// <summary>True if the repo's git config references a github.com remote.</summary>
    public static bool HasGitHubRemote(string directory)
    {
        var configPath = ResolveConfigPath(directory);
        return configPath is not null && File.Exists(configPath) &&
            File.ReadAllText(configPath).Contains("github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveConfigPath(string directory)
    {
        var gitPath = Path.Combine(directory, ".git");
        if (Directory.Exists(gitPath))
        {
            return Path.Combine(gitPath, "config");
        }

        if (!File.Exists(gitPath))
        {
            return null;
        }

        // Worktrees/submodules: ".git" is a file containing "gitdir: <path-to-real-gitdir>".
        var content = File.ReadAllText(gitPath).Trim();
        const string prefix = "gitdir:";
        if (!content.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var target = content[prefix.Length..].Trim();
        var resolved = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(directory, target));
        return Path.Combine(resolved, "config");
    }
}
