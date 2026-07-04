using SteadyCron.Cli.Infrastructure;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class GitRepositoryHelperTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("steadycron-git-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void IsGitRepo_false_whenNoGitDirectory()
    {
        Assert.False(GitRepositoryHelper.IsGitRepo(_dir));
    }

    [Fact]
    public void IsGitRepo_true_whenGitDirectoryExists()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Assert.True(GitRepositoryHelper.IsGitRepo(_dir));
    }

    [Fact]
    public void IsGitRepo_true_forWorktreeGitFile()
    {
        File.WriteAllText(Path.Combine(_dir, ".git"), "gitdir: /somewhere/else\n");
        Assert.True(GitRepositoryHelper.IsGitRepo(_dir));
    }

    [Fact]
    public void HasGitHubRemote_false_whenNoGitDirectory()
    {
        Assert.False(GitRepositoryHelper.HasGitHubRemote(_dir));
    }

    [Fact]
    public void HasGitHubRemote_true_whenConfigReferencesGitHub()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        File.WriteAllText(Path.Combine(gitDir.FullName, "config"),
            "[remote \"origin\"]\n\turl = git@github.com:steadycron/cli.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");

        Assert.True(GitRepositoryHelper.HasGitHubRemote(_dir));
    }

    [Fact]
    public void HasGitHubRemote_false_whenConfigReferencesOtherHost()
    {
        var gitDir = Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        File.WriteAllText(Path.Combine(gitDir.FullName, "config"),
            "[remote \"origin\"]\n\turl = git@gitlab.com:example/repo.git\n");

        Assert.False(GitRepositoryHelper.HasGitHubRemote(_dir));
    }

    [Fact]
    public void HasGitHubRemote_resolvesWorktreeGitFile()
    {
        var realGitDir = Directory.CreateDirectory(Path.Combine(_dir, "real-gitdir"));
        File.WriteAllText(Path.Combine(realGitDir.FullName, "config"),
            "[remote \"origin\"]\n\turl = https://github.com/steadycron/cli.git\n");

        var worktreeDir = Directory.CreateDirectory(Path.Combine(_dir, "worktree")).FullName;
        File.WriteAllText(Path.Combine(worktreeDir, ".git"), $"gitdir: {realGitDir.FullName}\n");

        Assert.True(GitRepositoryHelper.HasGitHubRemote(worktreeDir));
    }
}
