using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class JobMapperTests
{
    private readonly JobMapper _mapper = new();

    // ── normalization / defaults ──────────────────────────────────────────────────

    [Fact]
    public void Http_defaults_match_the_api()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Schedule = "0 9 * * 1",
        }, 0);

        Assert.Equal("http", d.Kind);
        Assert.Equal("GET", d.HttpMethod);
        Assert.Equal("UTC", d.Timezone);
        Assert.Equal(60, d.TimeoutSeconds);
        Assert.Equal(0, d.MaxRetries);
        Assert.Equal(30, d.RetryBackoffSeconds);
        Assert.True(d.RetryOnTimeout);
        Assert.False(d.SkipIfRunning);
        Assert.Equal("do_nothing", d.MisfirePolicyValue);
        Assert.Null(d.RetryOnStatusCodes);
        Assert.Equal(ScheduleKind.Cron, d.ScheduleKind);
    }

    [Fact]
    public void Heartbeat_defaults_match_the_api()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "hb",
            Kind = "heartbeat",
            Interval = 3600,
        }, 0);

        Assert.Equal("heartbeat", d.Kind);
        Assert.Equal(60, d.GraceSeconds);
        Assert.True(d.StuckRunDetection);
        Assert.Null(d.MaxRunDurationSeconds);
        Assert.Equal(ScheduleKind.Interval, d.ScheduleKind);
        Assert.Equal(3600, d.IntervalSeconds);
    }

    [Fact]
    public void Status_codes_are_normalized_sorted_distinct()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 60,
            RetryOnStatus = [503, 500, 500, 502],
        }, 0);

        Assert.Equal(new[] { 500, 502, 503 }, d.RetryOnStatusCodes);
    }

    // ── validation ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("name only")] // missing url + schedule
    public void Http_requires_url(string _)
    {
        var ex = Assert.Throws<ManifestException>(() =>
            _mapper.ToDesired(new ManifestJob { Name = "job", Schedule = "* * * * *" }, 0));
        Assert.Contains("url", ex.Message);
    }

    [Fact]
    public void Cannot_specify_both_schedule_and_interval()
    {
        Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Schedule = "* * * * *",
            Interval = 60,
        }, 0));
    }

    [Fact]
    public void Requires_a_schedule()
    {
        Assert.Throws<ManifestException>(() =>
            _mapper.ToDesired(new ManifestJob { Name = "job", Url = "https://example.com" }, 0));
    }

    [Fact]
    public void Heartbeat_rejects_http_fields()
    {
        var ex = Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "hb",
            Kind = "heartbeat",
            Interval = 300,
            Url = "https://example.com",
        }, 0));
        Assert.Contains("ping-driven", ex.Message);
        Assert.Contains("url", ex.Message);
    }

    [Fact]
    public void Rejects_invalid_method()
    {
        Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 60,
            Method = "FETCH",
        }, 0));
    }

    [Fact]
    public void Rejects_out_of_range_interval()
    {
        Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 5,
        }, 0));
    }

    // ── create payloads ───────────────────────────────────────────────────────────

    [Fact]
    public void Create_request_for_http_carries_paused_status()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 300,
            Paused = true,
        }, 0);

        var request = _mapper.ToCreateRequest(d);

        Assert.Equal("http", request.Kind);
        Assert.Equal(ExecutionStatus.Paused, request.Status);
        Assert.Equal("https://example.com", request.HttpUrl);
        Assert.Equal(ScheduleKind.Interval, request.ScheduleKind);
    }

    [Fact]
    public void Create_request_for_heartbeat_sets_no_http_fields()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "hb",
            Kind = "heartbeat",
            Schedule = "0 2 * * *",
            Grace = 1800,
        }, 0);

        var request = _mapper.ToCreateRequest(d);

        Assert.Equal("heartbeat", request.Kind);
        Assert.Null(request.HttpUrl);
        Assert.Null(request.HttpMethod);
        Assert.Equal(1800, request.GraceSeconds);
    }

    // ── diffing ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Identical_state_produces_no_changes()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Method = "POST",
            Schedule = "0 9 * * 1",
            Timezone = "Europe/Berlin",
            Timeout = 120,
            Retries = 3,
        }, 0);

        var server = ServerFromHttp(d);

        var result = _mapper.BuildUpdate(d, server);

        Assert.False(result.HasChanges);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Detects_changed_url_and_timeout()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://new.example.com",
            Interval = 300,
            Timeout = 90,
        }, 0);

        var server = ServerFromHttp(d) with { HttpUrl = "https://old.example.com", TimeoutSeconds = 60 };

        var result = _mapper.BuildUpdate(d, server);

        Assert.True(result.HasChanges);
        Assert.Contains(result.Changes, c => c.Field == "url");
        Assert.Contains(result.Changes, c => c.Field == "timeout");
        Assert.Equal("https://new.example.com", result.Request.HttpUrl);
        Assert.Equal(90, result.Request.TimeoutSeconds);
    }

    [Fact]
    public void Detects_changed_runbook_notes_and_url()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Schedule = "0 9 * * 1",
            RunbookNotes = "## Restart steps\n1. Restart the worker.",
            RunbookUrl = "https://runbooks.example.com/job",
        }, 0);

        var server = ServerFromHttp(d) with { RunbookNotes = "old notes", RunbookUrl = "https://old.example.com/runbook" };

        var result = _mapper.BuildUpdate(d, server);

        Assert.True(result.HasChanges);
        Assert.Contains(result.Changes, c => c.Field == "runbook_notes");
        Assert.Contains(result.Changes, c => c.Field == "runbook_url");
        Assert.Equal(d.RunbookNotes, result.Request.RunbookNotes);
        Assert.Equal(d.RunbookUrl, result.Request.RunbookUrl);
    }

    [Fact]
    public void Schedule_change_cron_to_interval()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 600,
        }, 0);

        var server = ServerFromHttp(d) with
        {
            ScheduleKind = "cron",
            CronExpression = "0 9 * * 1",
            IntervalSeconds = null,
        };

        var result = _mapper.BuildUpdate(d, server);

        Assert.Contains(result.Changes, c => c.Field == "schedule");
        Assert.Equal(600, result.Request.IntervalSeconds);
        Assert.Null(result.Request.CronExpression);
    }

    [Fact]
    public void Clearing_headers_sends_empty_dict()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 300,
        }, 0);

        var server = ServerFromHttp(d) with
        {
            HttpHeaders = new Dictionary<string, string> { ["X-Old"] = "1" },
        };

        var result = _mapper.BuildUpdate(d, server);

        Assert.Contains(result.Changes, c => c.Field == "headers");
        Assert.NotNull(result.Request.HttpHeaders);
        Assert.Empty(result.Request.HttpHeaders!);
    }

    [Fact]
    public void Changing_only_status_codes_still_sends_retry_on_timeout()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 300,
            RetryOnStatus = [500, 502],
        }, 0);

        var server = ServerFromHttp(d) with { RetryOnStatusCodes = null };

        var result = _mapper.BuildUpdate(d, server);

        Assert.Contains(result.Changes, c => c.Field == "retry_on_status");
        Assert.NotNull(result.Request.RetryOnTimeout); // paired so the server applies codes
        Assert.Equal(new[] { 500, 502 }, result.Request.RetryOnStatusCodes);
    }

    [Fact]
    public void Clearing_status_codes_omits_them_so_server_stores_null()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "job",
            Url = "https://example.com",
            Interval = 300,
        }, 0);

        var server = ServerFromHttp(d) with { RetryOnStatusCodes = [500, 502] };

        var result = _mapper.BuildUpdate(d, server);

        Assert.Contains(result.Changes, c => c.Field == "retry_on_status");
        Assert.NotNull(result.Request.RetryOnTimeout);
        Assert.Null(result.Request.RetryOnStatusCodes); // omitted → server sets null (any non-2xx)
    }

    [Fact]
    public void Heartbeat_max_run_duration_only_diffed_when_specified()
    {
        var omitted = _mapper.ToDesired(new ManifestJob
        {
            Name = "hb",
            Kind = "heartbeat",
            Interval = 3600,
        }, 0);

        var server = ServerFromHeartbeat(omitted) with { MaxRunDurationSeconds = 9999 };

        // Not specified in manifest → server's computed value is left alone.
        Assert.False(_mapper.BuildUpdate(omitted, server).HasChanges);

        var specified = _mapper.ToDesired(new ManifestJob
        {
            Name = "hb",
            Kind = "heartbeat",
            Interval = 3600,
            MaxRunDurationSeconds = 1200,
        }, 0);

        var result = _mapper.BuildUpdate(specified, server);
        Assert.Contains(result.Changes, c => c.Field == "max_run_duration_seconds");
        Assert.Equal(1200, result.Request.MaxRunDurationSeconds);
    }

    // ── agent monitors ────────────────────────────────────────────────────────────

    [Fact]
    public void Agent_defaults_match_the_api()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
        }, 0);

        Assert.Equal("agent", d.Kind);
        Assert.True(d.IsAgent);
        Assert.False(d.IsHttp);
        Assert.True(d.ReportRequired);
        Assert.True(d.RuleEmptyResultEnabled);
        // Forced on: enforcing /start is what the kind is for.
        Assert.True(d.StuckRunDetection);
        Assert.Null(d.ItemsLabel);
        Assert.Null(d.RuleMaxCostUsdPerRun);
        Assert.Null(d.RuleCostPeriod);
    }

    [Fact]
    public void Agent_carries_outcome_rules_into_the_create_request()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            ItemsLabel = "tickets",
            ReportRequired = false,
            RuleEmptyResultEnabled = false,
            RuleMaxCostUsdPerRun = 0.5m,
            RuleMaxCostUsdPerPeriod = 20m,
            RuleCostPeriod = "day",
            RuleMaxSteps = 40,
            RuleMaxToolCalls = 100,
            RuleMaxDurationMs = 900_000,
        }, 0);

        var request = _mapper.ToCreateRequest(d);

        Assert.Equal("agent", request.Kind);
        Assert.Equal("tickets", request.ItemsLabel);
        Assert.False(request.ReportRequired);
        Assert.False(request.RuleEmptyResultEnabled);
        Assert.Equal(0.5m, request.RuleMaxCostUsdPerRun);
        Assert.Equal(20m, request.RuleMaxCostUsdPerPeriod);
        Assert.Equal(AgentCostPeriod.Day, request.RuleCostPeriod);
        Assert.Equal(40, request.RuleMaxSteps);
        Assert.Equal(100, request.RuleMaxToolCalls);
        Assert.Equal(900_000, request.RuleMaxDurationMs);
        // The API forces it true and rejects the field; sending it would be noise at best.
        Assert.Null(request.StuckRunDetection);
    }

    [Fact]
    public void Agent_defaults_the_cost_period_to_month_when_a_period_ceiling_is_set()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            RuleMaxCostUsdPerPeriod = 20m,
        }, 0);

        Assert.Equal(AgentCostPeriod.Month, d.RuleCostPeriod);
    }

    [Fact]
    public void Agent_treats_a_zero_ceiling_as_no_ceiling()
    {
        var d = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            RuleMaxCostUsdPerRun = 0m,
            RuleMaxSteps = 0,
        }, 0);

        Assert.Null(d.RuleMaxCostUsdPerRun);
        Assert.Null(d.RuleMaxSteps);
    }

    [Fact]
    public void Agent_rejects_disabling_stuck_run_detection()
    {
        var ex = Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            StuckRunDetection = false,
        }, 0));

        Assert.Contains("stuck_run_detection", ex.Message);
    }

    [Fact]
    public void Agent_rejects_an_overlong_items_label()
    {
        var ex = Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            ItemsLabel = new string('x', 41),
        }, 0));

        Assert.Contains("items_label", ex.Message);
    }

    [Fact]
    public void Agent_rejects_a_cost_period_without_a_period_ceiling()
    {
        var ex = Assert.Throws<ManifestException>(() => _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            RuleCostPeriod = "month",
        }, 0));

        Assert.Contains("rule_cost_period", ex.Message);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("heartbeat")]
    public void Agent_fields_are_rejected_on_other_kinds(string kind)
    {
        var job = new ManifestJob
        {
            Name = "job",
            Kind = kind,
            Interval = 300,
            ItemsLabel = "tickets",
        };

        if (kind == "http") { job.Url = "https://example.com"; }

        var ex = Assert.Throws<ManifestException>(() => _mapper.ToDesired(job, 0));
        Assert.Contains("items_label", ex.Message);
        Assert.Contains("agent monitors", ex.Message);
    }

    [Fact]
    public void Agent_diff_sends_zero_to_clear_a_ceiling()
    {
        var desired = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
        }, 0);

        var server = ServerFromAgent(desired) with { RuleMaxCostUsdPerRun = 0.5m, RuleMaxSteps = 40 };

        var result = _mapper.BuildUpdate(desired, server);

        Assert.True(result.HasChanges);
        Assert.Equal(0m, result.Request.RuleMaxCostUsdPerRun);
        Assert.Equal(0, result.Request.RuleMaxSteps);
    }

    [Fact]
    public void Agent_diff_reports_write_once_fields_without_sending_them()
    {
        var desired = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
            ReportRequired = false,
        }, 0);

        var server = ServerFromAgent(desired) with { ReportRequired = true };

        var result = _mapper.BuildUpdate(desired, server);

        // PATCH cannot express it (docs/AGENTS.md §8) — surface the drift, don't pretend it applied.
        Assert.Contains(result.Changes, c => c.Field.StartsWith("report_required", StringComparison.Ordinal));
    }

    [Fact]
    public void Agent_diff_ignores_stuck_run_detection()
    {
        var desired = _mapper.ToDesired(new ManifestJob
        {
            Name = "triage",
            Kind = "agent",
            Schedule = "0 3 * * *",
        }, 0);

        var server = ServerFromAgent(desired) with { StuckRunDetection = false };

        Assert.DoesNotContain(_mapper.BuildUpdate(desired, server).Changes, c => c.Field == "stuck_run_detection");
    }

    // ── helpers: build a server JobResponse that matches a desired job exactly ─────

    private static JobResponse ServerFromHttp(DesiredJob d) => new()
    {
        Id = Guid.NewGuid(),
        Kind = "http",
        Name = d.Name,
        Description = d.Description,
        RunbookNotes = d.RunbookNotes,
        RunbookUrl = d.RunbookUrl,
        ScheduleKind = d.ScheduleKind == ScheduleKind.Cron ? "cron" : "interval",
        CronExpression = d.CronExpression,
        IntervalSeconds = d.IntervalSeconds,
        Timezone = d.Timezone,
        HttpMethod = d.HttpMethod,
        HttpUrl = d.HttpUrl,
        HttpHeaders = d.HttpHeaders.Count > 0 ? new Dictionary<string, string>(d.HttpHeaders) : null,
        HttpBody = d.HttpBody,
        TimeoutSeconds = d.TimeoutSeconds,
        MaxRetries = d.MaxRetries,
        RetryBackoffSeconds = d.RetryBackoffSeconds,
        RetryOnTimeout = d.RetryOnTimeout,
        RetryOnStatusCodes = d.RetryOnStatusCodes?.ToArray(),
        SkipIfRunning = d.SkipIfRunning,
        MisfirePolicy = d.MisfirePolicyValue,
    };

    private static JobResponse ServerFromHeartbeat(DesiredJob d) => new()
    {
        Id = Guid.NewGuid(),
        Kind = "heartbeat",
        Name = d.Name,
        Description = d.Description,
        RunbookNotes = d.RunbookNotes,
        RunbookUrl = d.RunbookUrl,
        ScheduleKind = d.ScheduleKind == ScheduleKind.Cron ? "cron" : "interval",
        CronExpression = d.CronExpression,
        IntervalSeconds = d.IntervalSeconds,
        Timezone = d.Timezone,
        GraceSeconds = d.GraceSeconds,
        StuckRunDetection = d.StuckRunDetection,
        MaxRunDurationSeconds = d.MaxRunDurationSeconds ?? 0,
    };

    private static JobResponse ServerFromAgent(DesiredJob d) => new()
    {
        Id = Guid.NewGuid(),
        Kind = "agent",
        Name = d.Name,
        Description = d.Description,
        RunbookNotes = d.RunbookNotes,
        RunbookUrl = d.RunbookUrl,
        ScheduleKind = d.ScheduleKind == ScheduleKind.Cron ? "cron" : "interval",
        CronExpression = d.CronExpression,
        IntervalSeconds = d.IntervalSeconds,
        Timezone = d.Timezone,
        GraceSeconds = d.GraceSeconds,
        StuckRunDetection = d.StuckRunDetection,
        MaxRunDurationSeconds = d.MaxRunDurationSeconds ?? 0,
        ReportRequired = d.ReportRequired,
        ItemsLabel = d.ItemsLabel,
        RuleEmptyResultEnabled = d.RuleEmptyResultEnabled,
        RuleMaxCostUsdPerRun = d.RuleMaxCostUsdPerRun,
        RuleMaxCostUsdPerPeriod = d.RuleMaxCostUsdPerPeriod,
        RuleCostPeriod = d.RuleCostPeriod?.ToString().ToLowerInvariant(),
        RuleMaxSteps = d.RuleMaxSteps,
        RuleMaxToolCalls = d.RuleMaxToolCalls,
        RuleMaxDurationMs = d.RuleMaxDurationMs,
    };
}
