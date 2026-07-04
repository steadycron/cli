using SteadyCron.Cli.Manifest.Generators;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class CronScheduleHelperTests
{
    private static readonly DateTimeOffset Baseline = new(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);

    // ── ComputeGraceDefault (SPEC-20c §3.5 / SPEC-21 §3.3) ────────────────────────

    [Fact]
    public void ComputeGraceDefault_fallsBackTo1800_withFewerThanTwoFires()
    {
        Assert.Equal(1800, CronScheduleHelper.ComputeGraceDefault([Baseline]));
        Assert.Equal(1800, CronScheduleHelper.ComputeGraceDefault([]));
    }

    [Fact]
    public void ComputeGraceDefault_subHourGap_uses300()
    {
        var fires = new[] { Baseline, Baseline.AddMinutes(15), Baseline.AddMinutes(30) };
        Assert.Equal(300, CronScheduleHelper.ComputeGraceDefault(fires));
    }

    [Fact]
    public void ComputeGraceDefault_subDayGap_uses1800()
    {
        var fires = new[] { Baseline, Baseline.AddHours(2), Baseline.AddHours(4) };
        Assert.Equal(1800, CronScheduleHelper.ComputeGraceDefault(fires));
    }

    [Fact]
    public void ComputeGraceDefault_dailyOrSlowerGap_uses3600()
    {
        var fires = new[] { Baseline, Baseline.AddDays(1), Baseline.AddDays(2) };
        Assert.Equal(3600, CronScheduleHelper.ComputeGraceDefault(fires));
    }

    [Fact]
    public void ComputeGraceDefault_usesTheTightestGap_notTheFirst()
    {
        var fires = new[] { Baseline, Baseline.AddDays(2), Baseline.AddDays(2).AddMinutes(10) };
        Assert.Equal(300, CronScheduleHelper.ComputeGraceDefault(fires));
    }

    // ── Validate (offline, Cronos-backed) ─────────────────────────────────────────

    [Theory]
    [InlineData("*/15 * * * *")]
    [InlineData("0 2 * * *")]
    [InlineData("0 9 * * 1-5")]
    public void Validate_acceptsWellFormedExpressions(string expression)
    {
        CronScheduleHelper.Validate(expression);
    }

    [Theory]
    [InlineData("not a cron expression")]
    [InlineData("99 * * * *")]
    [InlineData("* * * * * *")] // 6-field (seconds) not supported — this CLI is 5-field only
    public void Validate_throwsFormatException_onInvalidExpressions(string expression)
    {
        Assert.Throws<FormatException>(() => CronScheduleHelper.Validate(expression));
    }

    // ── GetNextFires (offline, Cronos-backed) ─────────────────────────────────────

    [Fact]
    public void GetNextFires_everyFifteenMinutes_returnsExpectedGaps()
    {
        var fires = CronScheduleHelper.GetNextFires("*/15 * * * *", "UTC", 3);

        Assert.Equal(3, fires.Count);
        Assert.Equal(TimeSpan.FromMinutes(15), fires[1] - fires[0]);
        Assert.Equal(TimeSpan.FromMinutes(15), fires[2] - fires[1]);
    }

    [Fact]
    public void GetNextFires_daily_yieldsTwentyFourHourGaps()
    {
        var fires = CronScheduleHelper.GetNextFires("0 2 * * *", "UTC", 3);

        Assert.Equal(3, fires.Count);
        Assert.Equal(TimeSpan.FromHours(24), fires[1] - fires[0]);
    }

    [Fact]
    public void GetNextFires_respectsNonUtcTimezone()
    {
        // 09:00 Europe/Berlin is 07:00 or 08:00 UTC depending on DST — either way, never 09:00 UTC.
        var fires = CronScheduleHelper.GetNextFires("0 9 * * *", "Europe/Berlin", 1);

        Assert.NotEqual(9, fires[0].UtcDateTime.Hour);
    }
}
