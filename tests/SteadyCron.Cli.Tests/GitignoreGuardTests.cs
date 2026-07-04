using SteadyCron.Cli.Infrastructure;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class GitignoreGuardTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"steadycron-gitignore-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void EnsureSecretsIgnored_createsFile_whenMissing()
    {
        var changed = GitignoreGuard.EnsureSecretsIgnored(_path);

        Assert.True(changed);
        var content = File.ReadAllText(_path);
        // *.env alone already matches ManifestEnvironment.DefaultSecretsFile ("steadycron_secrets.env");
        // .env.* additionally covers the .env.local / .env.production dotenv convention.
        Assert.Contains("*.env", content, StringComparison.Ordinal);
        Assert.Contains(".env.*", content, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSecretsIgnored_appendsToExistingFile_preservingItsContent()
    {
        File.WriteAllText(_path, "node_modules/\ndist/\n");

        var changed = GitignoreGuard.EnsureSecretsIgnored(_path);

        Assert.True(changed);
        var content = File.ReadAllText(_path);
        Assert.StartsWith("node_modules/\ndist/\n", content, StringComparison.Ordinal);
        Assert.Contains("*.env", content, StringComparison.Ordinal);
        Assert.Contains(".env.*", content, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSecretsIgnored_handlesMissingTrailingNewline()
    {
        File.WriteAllText(_path, "node_modules/"); // no trailing newline

        GitignoreGuard.EnsureSecretsIgnored(_path);

        var content = File.ReadAllText(_path);
        Assert.DoesNotContain("node_modules/*.env", content, StringComparison.Ordinal);
        Assert.Contains("node_modules/\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSecretsIgnored_isIdempotent_secondRunChangesNothing()
    {
        GitignoreGuard.EnsureSecretsIgnored(_path);
        var afterFirst = File.ReadAllText(_path);

        var changed = GitignoreGuard.EnsureSecretsIgnored(_path);

        Assert.False(changed);
        Assert.Equal(afterFirst, File.ReadAllText(_path));
    }

    [Fact]
    public void EnsureSecretsIgnored_returnsFalse_whenEntriesAlreadyPresent()
    {
        File.WriteAllText(_path, "*.env\n.env.*\n");

        var changed = GitignoreGuard.EnsureSecretsIgnored(_path);

        Assert.False(changed);
    }

    [Fact]
    public void EnsureSecretsIgnored_addsOnlyTheMissingEntry()
    {
        File.WriteAllText(_path, "*.env\n"); // already has one pattern, missing the dotenv-family one

        var changed = GitignoreGuard.EnsureSecretsIgnored(_path);

        Assert.True(changed);
        var content = File.ReadAllText(_path);
        Assert.Single(content.Split('\n', StringSplitOptions.RemoveEmptyEntries), l => l.Trim() == "*.env");
        Assert.Contains(".env.*", content, StringComparison.Ordinal);
    }
}
