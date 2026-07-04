using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Commands;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// SPEC-20c: unit coverage for the pieces of the `init` wizard that are pure logic (grace default
/// derivation, alert-confirmation wording). The wizard's `RunAsync` itself requires an interactive
/// terminal (<c>TerminalHelper.IsInteractive()</c>), which is never true under the test runner —
/// same constraint <see cref="SignupHelpersTests"/> works around by testing the extracted static
/// helpers rather than the command entry point.
/// </summary>
public sealed class InitWizardCommandTests
{
    private static readonly DateTimeOffset Baseline = new(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeGraceDefault_fallsBackTo1800_withFewerThanTwoFires()
    {
        Assert.Equal(1800, InitWizardCommand.ComputeGraceDefault([Baseline]));
        Assert.Equal(1800, InitWizardCommand.ComputeGraceDefault([]));
    }

    [Fact]
    public void ComputeGraceDefault_subHourGap_uses300()
    {
        var fires = new[] { Baseline, Baseline.AddMinutes(15), Baseline.AddMinutes(30) };
        Assert.Equal(300, InitWizardCommand.ComputeGraceDefault(fires));
    }

    [Fact]
    public void ComputeGraceDefault_subDayGap_uses1800()
    {
        var fires = new[] { Baseline, Baseline.AddHours(2), Baseline.AddHours(4) };
        Assert.Equal(1800, InitWizardCommand.ComputeGraceDefault(fires));
    }

    [Fact]
    public void ComputeGraceDefault_dailyOrSlowerGap_uses3600()
    {
        var fires = new[] { Baseline, Baseline.AddDays(1), Baseline.AddDays(2) };
        Assert.Equal(3600, InitWizardCommand.ComputeGraceDefault(fires));
    }

    [Fact]
    public void ComputeGraceDefault_usesTheTightestGap_notTheFirst()
    {
        // First gap is 2 days (>=24h tier); second gap is 10 minutes (<1h tier) — the tightest
        // gap governs, since that's the one most likely to false-positive a missed heartbeat.
        var fires = new[] { Baseline, Baseline.AddDays(2), Baseline.AddDays(2).AddMinutes(10) };
        Assert.Equal(300, InitWizardCommand.ComputeGraceDefault(fires));
    }

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
}
