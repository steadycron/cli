using Spectre.Console.Testing;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Commands;
using SteadyCron.Cli.Output;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// SPEC-20c: unit coverage for the pieces of the `init` wizard that are pure logic
/// (alert-confirmation wording). The wizard's `RunAsync` itself requires an interactive terminal
/// (<c>TerminalHelper.IsInteractive()</c>), which is never true under the test runner — same
/// constraint <see cref="SignupHelpersTests"/> works around by testing the extracted static
/// helpers rather than the command entry point.
/// </summary>
[Collection("ConsoleOutRedirection")]
public sealed class InitWizardCommandTests
{
    [Fact]
    public void BuildAlertConfirmation_heartbeat_mentionsPingAndChannelEmail()
    {
        var channel = new AlertChannelResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Email (default)", "email",
            new Dictionary<string, string> { ["to"] = "ops@example.com" }, DateTimeOffset.UtcNow);

        var line = InitWizardCommand.BuildAlertConfirmation("heartbeat", channel);

        Assert.Equal("If a ping doesn't arrive on time, we'll email ops@example.com.", line);
    }

    [Fact]
    public void BuildAlertConfirmation_http_mentionsFailureAndChannelEmail()
    {
        var channel = new AlertChannelResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Email (default)", "email",
            new Dictionary<string, string> { ["to"] = "ops@example.com" }, DateTimeOffset.UtcNow);

        var line = InitWizardCommand.BuildAlertConfirmation("http", channel);

        Assert.Equal("If a run fails, we'll email ops@example.com.", line);
    }

    [Fact]
    public void BuildAlertConfirmation_fallsBackToGenericWording_whenChannelConfigUnreadable()
    {
        var channel = new AlertChannelResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Email (default)", "email", Config: null, DateTimeOffset.UtcNow);

        var line = InitWizardCommand.BuildAlertConfirmation("heartbeat", channel);

        Assert.Equal("If a ping doesn't arrive on time, we'll email your default alert channel.", line);
    }

    [Fact]
    public void BuildAlertConfirmation_nonEmailChannel_usesGenericWording()
    {
        var channel = new AlertChannelResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Ops Slack", "slack",
            new Dictionary<string, string> { ["webhook_url"] = "https://hooks.slack.com/x" }, DateTimeOffset.UtcNow);

        var line = InitWizardCommand.BuildAlertConfirmation("http", channel);

        Assert.Equal("If a run fails, we'll email your default alert channel.", line);
    }

    // ── README badge snippet ───────────────────────────────────────────────────────

    private static JobResponse Job(string kind, string? badgeUrl, PingUrls? pingUrls) => new()
    {
        Id = Guid.NewGuid(),
        Name = "nightly-backup",
        Kind = kind,
        JobKey = "nightly-backup",
        BadgeUrl = badgeUrl,
        PingUrls = pingUrls,
        ScheduleKind = "cron",
        Timezone = "UTC",
    };

    /// <summary>Runs <paramref name="action"/> with both a <see cref="TestConsole"/> (for
    /// <c>Markup</c>/<c>Line</c> calls) and a captured real <c>Console.Out</c> (for <c>RawLine</c>,
    /// which bypasses the injected console entirely by design — see <see cref="OutputContext.RawLine"/>).</summary>
    private static (string TestConsoleOutput, string RawOutput) CaptureOutput(Action<OutputContext> action)
    {
        var console = new TestConsole();
        var output = new OutputContext(json: false, quiet: false, console, console);

        var originalOut = Console.Out;
        var rawWriter = new StringWriter();
        Console.SetOut(rawWriter);
        try
        {
            action(output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        return (console.Output, rawWriter.ToString());
    }

    [Fact]
    public void PrintReadmeBadgeSnippet_noBadgeUrl_printsNothing()
    {
        var (testConsoleOutput, rawOutput) = CaptureOutput(
            o => InitWizardCommand.PrintReadmeBadgeSnippet(o, Job("heartbeat", badgeUrl: null, pingUrls: null)));

        Assert.Equal(string.Empty, testConsoleOutput);
        Assert.Equal(string.Empty, rawOutput);
    }

    [Fact]
    public void PrintReadmeBadgeSnippet_heartbeat_includesBadgePingAndStatusCommand()
    {
        var pingUrls = new PingUrls("https://ping.steadycron.com/abc/success", "https://ping.steadycron.com/abc/start", "https://ping.steadycron.com/abc/fail");

        var (testConsoleOutput, rawOutput) = CaptureOutput(
            o => InitWizardCommand.PrintReadmeBadgeSnippet(o, Job("heartbeat", "https://api.steadycron.com/badge/abc.svg", pingUrls)));

        Assert.Equal(
            "[![nightly-backup](https://api.steadycron.com/badge/abc.svg)](https://steadycron.com)",
            rawOutput.Trim());
        Assert.Contains("curl -fsS https://ping.steadycron.com/abc/success", testConsoleOutput, StringComparison.Ordinal);
        Assert.Contains("steadycron jobs get nightly-backup", testConsoleOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintReadmeBadgeSnippet_httpJob_omitsPingLine_hasNoPingUrls()
    {
        var (testConsoleOutput, rawOutput) = CaptureOutput(
            o => InitWizardCommand.PrintReadmeBadgeSnippet(o, Job("http", "https://api.steadycron.com/badge/abc.svg", pingUrls: null)));

        Assert.Contains("api.steadycron.com/badge/abc.svg", rawOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Pings on success", testConsoleOutput, StringComparison.Ordinal);
        Assert.Contains("steadycron jobs get nightly-backup", testConsoleOutput, StringComparison.Ordinal);
    }

    // ── CI workflow template ───────────────────────────────────────────────────────

    [Fact]
    public void CiWorkflowTemplate_referencesTheActionAndApiKeySecret()
    {
        Assert.Contains("uses: steadycron/action@v1", InitWizardCommand.CiWorkflowTemplate, StringComparison.Ordinal);
        Assert.Contains("STEADYCRON_API_KEY", InitWizardCommand.CiWorkflowTemplate, StringComparison.Ordinal);
        Assert.Contains("manifest: steadycron.yaml", InitWizardCommand.CiWorkflowTemplate, StringComparison.Ordinal);
        Assert.Contains("pull-requests: write", InitWizardCommand.CiWorkflowTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void CiWorkflowTemplate_selectsPlanOnPullRequest_applyOtherwise()
    {
        Assert.Contains(
            "github.event_name == 'pull_request' && 'plan' || 'apply'",
            InitWizardCommand.CiWorkflowTemplate, StringComparison.Ordinal);
    }
}
