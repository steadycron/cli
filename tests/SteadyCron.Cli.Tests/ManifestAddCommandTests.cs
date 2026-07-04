using SteadyCron.Cli.Commands.Manifest;
using SteadyCron.Cli.Output;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// SPEC-21: end-to-end coverage of the `manifest add` commands via flags only (no prompts) —
/// the non-interactive path the test runner can actually drive, since these commands prompt via
/// the static <c>Spectre.Console.AnsiConsole</c> (matching <c>manifest scaffold</c>/<c>import</c>'s
/// existing convention), which needs a real terminal to test interactively. The prompt-mode path
/// was verified manually (see conversation) by driving the wizard through a pseudo-tty. Each test
/// runs against an absolute temp file path (via <c>-f</c>) so nothing depends on or mutates the
/// process's working directory.
/// </summary>
public sealed class ManifestAddCommandTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"steadycron-add-test-{Guid.NewGuid():N}.yaml");

    public void Dispose() => File.Delete(_path);

    // ── job ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddJob_flagsOnly_writesHeartbeatJobAndPasses_Validate()
    {
        var settings = new AddJobSettings
        {
            File = _path,
            Kind = "heartbeat",
            Name = "nightly-backup",
            Schedule = "0 2 * * *",
            Grace = 600,
        };

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        var content = await File.ReadAllTextAsync(_path);
        Assert.Contains("- id: nightly-backup", content, StringComparison.Ordinal);
        Assert.Contains("kind: heartbeat", content, StringComparison.Ordinal);
        Assert.Contains("grace: 600", content, StringComparison.Ordinal);
        AssertValidates(content);
    }

    [Fact]
    public async Task AddJob_httpKind_requiresUrl_flagsOnly()
    {
        var settings = new AddJobSettings
        {
            File = _path,
            Kind = "http",
            Name = "daily-report",
            Schedule = "0 9 * * 1-5",
            Url = "https://api.example.com/report",
            Method = "post",
        };

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        var content = await File.ReadAllTextAsync(_path);
        Assert.Contains("kind: http", content, StringComparison.Ordinal);
        Assert.Contains("method: POST", content, StringComparison.Ordinal); // normalized to uppercase
        Assert.Contains("url: https://api.example.com/report", content, StringComparison.Ordinal);
        AssertValidates(content);
    }

    [Fact]
    public async Task AddJob_duplicateName_failsWithExitCode2_fileUntouched()
    {
        var settings = new AddJobSettings { File = _path, Kind = "heartbeat", Name = "dup-job", Schedule = "0 2 * * *" };
        await new AddJobCommand().ExecuteAsync(null!, settings);
        var before = await File.ReadAllTextAsync(_path);

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.ManifestError, exit);
        Assert.Equal(before, await File.ReadAllTextAsync(_path));
    }

    [Fact]
    public async Task AddJob_scheduleAndInterval_bothGiven_isRejected()
    {
        var settings = new AddJobSettings
        {
            File = _path, Kind = "heartbeat", Name = "x", Schedule = "0 2 * * *", Interval = 300,
        };

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task AddJob_invalidCronSyntax_isRejected_beforeAnyWrite()
    {
        var settings = new AddJobSettings { File = _path, Kind = "heartbeat", Name = "x", Schedule = "not a cron" };

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task AddJob_terraformFlag_isRejected_exitCode1()
    {
        var settings = new AddJobSettings { File = _path, Kind = "heartbeat", Name = "x", Schedule = "0 2 * * *", Terraform = true };

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task AddJob_dryRun_printsBlock_writesNothing()
    {
        var settings = new AddJobSettings { File = _path, Kind = "heartbeat", Name = "x", Schedule = "0 2 * * *", DryRun = true };

        var exit = await new AddJobCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task AddJob_targetIsADirectory_isRejected()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), $"steadycron-add-test-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dirPath);
        try
        {
            var settings = new AddJobSettings { File = dirPath, Kind = "heartbeat", Name = "x", Schedule = "0 2 * * *" };
            var exit = await new AddJobCommand().ExecuteAsync(null!, settings);
            Assert.Equal(ExitCodes.Error, exit);
        }
        finally
        {
            Directory.Delete(dirPath);
        }
    }

    // ── channel ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("slack", "OPS_SLACK_WEBHOOK_URL")]
    [InlineData("discord", "OPS_SLACK_WEBHOOK_URL")]
    [InlineData("webhook", "OPS_SLACK_URL")]
    public async Task AddChannel_secretBearingKinds_emitEnvPlaceholder_neverARealValue(string kind, string _)
    {
        var settings = new AddChannelSettings { File = _path, Kind = kind, Name = "ops-slack" };

        var exit = await new AddChannelCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        var content = await File.ReadAllTextAsync(_path);
        Assert.Contains("${OPS_SLACK_", content, StringComparison.Ordinal);
        AssertValidates(content);
    }

    [Fact]
    public async Task AddChannel_email_usesProvidedAddress_directly()
    {
        var settings = new AddChannelSettings { File = _path, Kind = "email", Name = "ops-email", To = "ops@example.com" };

        var exit = await new AddChannelCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        var content = await File.ReadAllTextAsync(_path);
        Assert.Contains("to: ops@example.com", content, StringComparison.Ordinal);
        Assert.DoesNotContain("${", content, StringComparison.Ordinal);
        AssertValidates(content);
    }

    [Fact]
    public async Task AddChannel_unknownKind_isRejected()
    {
        var settings = new AddChannelSettings { File = _path, Kind = "carrier-pigeon", Name = "x" };
        var exit = await new AddChannelCommand().ExecuteAsync(null!, settings);
        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(File.Exists(_path));
    }

    // ── tag ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTag_flagsOnly_writesTagWithColor()
    {
        var settings = new AddTagSettings { File = _path, Key = "env", Value = "staging", Color = "yellow" };

        var exit = await new AddTagCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        var content = await File.ReadAllTextAsync(_path);
        Assert.Contains("key: env", content, StringComparison.Ordinal);
        Assert.Contains("value: staging", content, StringComparison.Ordinal);
        Assert.Contains("color: yellow", content, StringComparison.Ordinal);
        AssertValidates(content);
    }

    [Fact]
    public async Task AddTag_sameKeyDifferentValue_isNotADuplicate()
    {
        await new AddTagCommand().ExecuteAsync(null!, new AddTagSettings { File = _path, Key = "env", Value = "staging" });

        var exit = await new AddTagCommand().ExecuteAsync(null!, new AddTagSettings { File = _path, Key = "env", Value = "production" });

        Assert.Equal(ExitCodes.Ok, exit);
    }

    [Fact]
    public async Task AddTag_invalidColor_isRejected()
    {
        var settings = new AddTagSettings { File = _path, Key = "env", Value = "staging", Color = "chartreuse" };
        var exit = await new AddTagCommand().ExecuteAsync(null!, settings);
        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(File.Exists(_path));
    }

    // ── variable ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddVariable_neverPromptsForOrEmitsARealValue()
    {
        var settings = new AddVariableSettings { File = _path, Name = "api_token" };

        var exit = await new AddVariableCommand().ExecuteAsync(null!, settings);

        Assert.Equal(ExitCodes.Ok, exit);
        var content = await File.ReadAllTextAsync(_path);
        Assert.Contains("value: ${API_TOKEN}", content, StringComparison.Ordinal);
        AssertValidates(content);
    }

    [Fact]
    public async Task AddVariable_duplicateName_isRejected()
    {
        await new AddVariableCommand().ExecuteAsync(null!, new AddVariableSettings { File = _path, Name = "api_token" });

        var exit = await new AddVariableCommand().ExecuteAsync(null!, new AddVariableSettings { File = _path, Name = "api_token" });

        Assert.Equal(ExitCodes.ManifestError, exit);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────

    private static void AssertValidates(string content)
    {
        var loader = new Manifest.ManifestLoader();
        var manifest = loader.Parse(content, "<test>", name => $"dummy-{name}");
        var result = new Manifest.ManifestValidator().Validate(manifest);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }
}
