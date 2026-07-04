using Spectre.Console.Testing;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Output;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class JobFormattingTests
{
    private static JobResponse Job(string kind, string? status) => new()
    {
        Id = Guid.NewGuid(),
        Name = "nightly-backup",
        Kind = kind,
        Status = status,
        ScheduleKind = "cron",
        CronExpression = "0 2 * * *",
        Timezone = "UTC",
    };

    // ── Pluralize ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0 jobs")]
    [InlineData(1, "1 job")]
    [InlineData(2, "2 jobs")]
    public void Pluralize_neverUsesJobParenS(int count, string expected)
    {
        Assert.Equal(expected, JobFormatting.Pluralize(count, "job"));
    }

    // ── Kind-aware status ────────────────────────────────────────────────────────

    [Fact]
    public void StatusMarkup_neverPingedHeartbeat_showsWaitingForPingInAmber()
    {
        var markup = JobFormatting.StatusMarkup(Job("heartbeat", status: null));

        Assert.Equal("[yellow]waiting for ping[/]", markup);
    }

    [Fact]
    public void StatusMarkup_neverRunHttpJob_showsAwaitingFirstRunInAmber()
    {
        var markup = JobFormatting.StatusMarkup(Job("http", status: null));

        Assert.Equal("[yellow]awaiting first run[/]", markup);
    }

    [Fact]
    public void StatusMarkup_knownStatus_delegatesToStringOverload()
    {
        var markup = JobFormatting.StatusMarkup(Job("heartbeat", status: "success"));

        Assert.Equal("[green]success[/]", markup);
    }

    // ── Shared jobs table ────────────────────────────────────────────────────────

    [Fact]
    public void RenderJobsTable_footer_pluralizesCorrectly()
    {
        var console = new TestConsole();
        var output = new OutputContext(json: false, quiet: false, console, console);

        JobFormatting.RenderJobsTable(output, [Job("heartbeat", "success")]);

        Assert.Contains("1 job.", console.Output);
        Assert.DoesNotContain("job(s)", console.Output);
    }

    [Fact]
    public void RenderJobsTable_multipleJobs_pluralizesFooter()
    {
        var console = new TestConsole();
        var output = new OutputContext(json: false, quiet: false, console, console);

        JobFormatting.RenderJobsTable(output, [Job("heartbeat", "success"), Job("http", null)]);

        Assert.Contains("2 jobs.", console.Output);
    }

    // ── Ping snippet styling (SPEC-20c §1/§4) ───────────────────────────────────

    [Fact]
    public void BuildPingSnippetLines_usesNoGreyOrDimMarkup_onlyBoldGreen()
    {
        var urls = new PingUrls("https://ping.steadycron.com/abc/success", "https://ping.steadycron.com/abc/start", "https://ping.steadycron.com/abc/fail");

        var lines = JobFormatting.BuildPingSnippetLines(urls, "0 2 * * *");
        var snippetLines = lines.Where(l => l.Contains("curl", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(snippetLines);
        Assert.All(snippetLines, l => Assert.Contains("[bold green]", l));
        Assert.All(lines, l => Assert.DoesNotContain("[grey]", l));
        Assert.All(lines, l => Assert.DoesNotContain("[dim]", l));
    }

    [Fact]
    public void BuildPingSnippetLines_pointsToPingRecipesDocs_andShowsOnlyOneVariant()
    {
        var urls = new PingUrls("https://ping.steadycron.com/abc/success", "https://ping.steadycron.com/abc/start", "https://ping.steadycron.com/abc/fail");

        var lines = JobFormatting.BuildPingSnippetLines(urls, "0 2 * * *");

        Assert.Contains(lines, l => l.Contains("steadycron.com/docs/ping-recipes", StringComparison.Ordinal));

        // Only the platform-detected variant appears — never both crontab and PowerShell.
        var otherPlatformMarker = OperatingSystem.IsWindows() ? "&& curl -fsS" : "your-script.ps1; if ($?)";
        Assert.DoesNotContain(lines, l => l.Contains(otherPlatformMarker, StringComparison.Ordinal));
    }
}
