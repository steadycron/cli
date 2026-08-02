using System.Text.Json;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>Locks the wire contract to the API: snake_case properties, snake_case enums, omitted nulls.</summary>
public sealed class JsonContractTests
{
    [Fact]
    public void CreateJobRequest_serializes_snake_case_with_enum_strings()
    {
        var request = new CreateJobRequest
        {
            Name = "digest",
            Kind = "http",
            ScheduleKind = ScheduleKind.Cron,
            CronExpression = "0 9 * * 1",
            Timezone = "Europe/Berlin",
            HttpMethod = "POST",
            HttpUrl = "https://example.com",
            TimeoutSeconds = 120,
            MisfirePolicy = MisfirePolicy.FireOnceNow,
            Status = ExecutionStatus.Paused,
        };

        var json = JsonSerializer.Serialize(request, SteadyCronJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("cron", root.GetProperty("schedule_kind").GetString());
        Assert.Equal("0 9 * * 1", root.GetProperty("cron_expression").GetString());
        Assert.Equal("POST", root.GetProperty("http_method").GetString());
        Assert.Equal("https://example.com", root.GetProperty("http_url").GetString());
        Assert.Equal(120, root.GetProperty("timeout_seconds").GetInt32());
        Assert.Equal("fire_once_now", root.GetProperty("misfire_policy").GetString());
        Assert.Equal("paused", root.GetProperty("status").GetString());
    }

    [Fact]
    public void CreateJobRequest_omits_null_fields()
    {
        var request = new CreateJobRequest
        {
            Name = "minimal",
            ScheduleKind = ScheduleKind.Interval,
            IntervalSeconds = 300,
        };

        var json = JsonSerializer.Serialize(request, SteadyCronJson.Options);

        Assert.DoesNotContain("description", json);
        Assert.DoesNotContain("http_url", json);
        Assert.DoesNotContain("cron_expression", json);
        Assert.Contains("interval_seconds", json);
    }

    [Fact]
    public void JobResponse_deserializes_from_snake_case_payload()
    {
        const string payload = """
        {
          "id": "0192a4e0-0000-7000-8000-000000000001",
          "account_id": "0192a4e0-0000-7000-8000-0000000000aa",
          "kind": "http",
          "name": "warm-cache",
          "status": "success",
          "schedule_kind": "interval",
          "interval_seconds": 900,
          "timezone": "UTC",
          "http_method": "GET",
          "http_url": "https://example.com/warm",
          "retry_on_timeout": true,
          "skip_if_running": true,
          "misfire_policy": "do_nothing",
          "max_retries": 2,
          "next_fire_at": "2026-06-03T10:00:00+00:00"
        }
        """;

        var job = JsonSerializer.Deserialize<JobResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(job);
        Assert.Equal("warm-cache", job!.Name);
        Assert.Equal("interval", job.ScheduleKind);
        Assert.Equal(900, job.IntervalSeconds);
        Assert.Equal("GET", job.HttpMethod);
        Assert.True(job.SkipIfRunning);
        Assert.Equal(2, job.MaxRetries);
        Assert.NotNull(job.NextFireAt);
    }

    [Fact]
    public void CreateJobRequest_serializes_agent_settings_with_the_api_field_names()
    {
        var request = new CreateJobRequest
        {
            Name = "nightly-triage",
            Kind = "agent",
            ScheduleKind = ScheduleKind.Cron,
            CronExpression = "0 3 * * *",
            ItemsLabel = "tickets",
            ReportRequired = false,
            RuleEmptyResultEnabled = false,
            RuleMaxCostUsdPerRun = 0.5m,
            RuleMaxCostUsdPerPeriod = 20m,
            RuleCostPeriod = AgentCostPeriod.Day,
            RuleMaxSteps = 40,
            RuleMaxToolCalls = 100,
            RuleMaxDurationMs = 900_000,
        };

        var json = JsonSerializer.Serialize(request, SteadyCronJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("agent", root.GetProperty("kind").GetString());
        Assert.Equal("tickets", root.GetProperty("items_label").GetString());
        Assert.False(root.GetProperty("report_required").GetBoolean());
        Assert.False(root.GetProperty("rule_empty_result_enabled").GetBoolean());
        Assert.Equal(0.5m, root.GetProperty("rule_max_cost_usd_per_run").GetDecimal());
        Assert.Equal(20m, root.GetProperty("rule_max_cost_usd_per_period").GetDecimal());
        Assert.Equal("day", root.GetProperty("rule_cost_period").GetString());
        Assert.Equal(40, root.GetProperty("rule_max_steps").GetInt32());
        Assert.Equal(100, root.GetProperty("rule_max_tool_calls").GetInt32());
        Assert.Equal(900_000, root.GetProperty("rule_max_duration_ms").GetInt32());
    }

    [Fact]
    public void JobResponse_deserializes_agent_settings_and_derives_the_kind_predicates()
    {
        const string payload = """
        {
          "id": "0192a4e0-0000-7000-8000-000000000002",
          "account_id": "0192a4e0-0000-7000-8000-0000000000aa",
          "kind": "agent",
          "name": "Nightly ticket triage",
          "status": "unverified",
          "schedule_kind": "cron",
          "cron_expression": "0 3 * * *",
          "timezone": "Europe/Berlin",
          "grace_seconds": 600,
          "stuck_run_detection": true,
          "max_run_duration_seconds": 1800,
          "misfire_policy": "do_nothing",
          "report_required": true,
          "items_label": "tickets",
          "rule_empty_result_enabled": true,
          "rule_max_cost_usd_per_run": 0.5,
          "rule_max_cost_usd_per_period": 20,
          "rule_cost_period": "month",
          "rule_max_steps": 40,
          "rule_max_tool_calls": 100,
          "rule_max_duration_ms": 900000
        }
        """;

        var job = JsonSerializer.Deserialize<JobResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(job);
        Assert.True(job!.IsAgent);
        Assert.True(job.IsPingDriven);
        Assert.Equal("tickets", job.ItemsLabel);
        Assert.True(job.ReportRequired);
        Assert.True(job.RuleEmptyResultEnabled);
        Assert.Equal(0.5m, job.RuleMaxCostUsdPerRun);
        Assert.Equal("month", job.RuleCostPeriod);
        Assert.Equal(900_000, job.RuleMaxDurationMs);
    }

    [Fact]
    public void JobResponse_kind_predicates_never_derive_one_from_the_other()
    {
        // The recurring bug docs/AGENTS.md §12 documents: `!= "heartbeat"` used to mean "HTTP".
        var agent = new JobResponse { Kind = "agent" };
        var heartbeat = new JobResponse { Kind = "heartbeat" };
        var http = new JobResponse { Kind = "http" };

        Assert.True(agent.IsPingDriven);
        Assert.True(heartbeat.IsPingDriven);
        Assert.False(http.IsPingDriven);

        Assert.True(JobKinds.IsHttp(http.Kind));
        Assert.False(JobKinds.IsHttp(agent.Kind));
        Assert.False(heartbeat.IsAgent);
    }

    [Fact]
    public void UpdateJobRequest_omits_the_write_once_agent_flags()
    {
        // report_required / rule_empty_result_enabled are not on the PATCH surface at all
        // (docs/AGENTS.md §8) — sending them would be silently ignored by the server.
        var json = JsonSerializer.Serialize(
            new UpdateJobRequest { ItemsLabel = "rows", RuleMaxCostUsdPerRun = 0m },
            SteadyCronJson.Options);

        Assert.Contains("\"items_label\":\"rows\"", json, StringComparison.Ordinal);
        // 0 is the documented "clear this ceiling" sentinel, so it must survive serialization.
        Assert.Contains("\"rule_max_cost_usd_per_run\":0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("report_required", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_empty_result_enabled", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertTrigger_serializes_the_agent_triggers_as_snake_case()
    {
        var json = JsonSerializer.Serialize(
            new CreateAlertRuleRequest { ChannelId = Guid.Empty, Trigger = AlertTrigger.OnEmptyResult },
            SteadyCronJson.Options);

        Assert.Contains("\"trigger\":\"on_empty_result\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CronPreviewResponse_maps_next_fires()
    {
        const string payload = """
        { "next_fires": ["2026-06-03T09:00:00+00:00", "2026-06-10T09:00:00+00:00"] }
        """;

        var response = JsonSerializer.Deserialize<CronPreviewResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(response);
        Assert.Equal(2, response!.NextFires.Count);
    }

    [Fact]
    public void ReconcileResponse_deserializes_server_plan_shape()
    {
        // Mirrors the documented /api/reconcile response (docs/CRON_AS_CODE.md): a per-action
        // summary plus a flat, action-keyed changes list. Regression guard for the contract drift
        // where the CLI read non-existent creates/updates arrays and reported every plan as empty.
        const string payload = """
        {
          "namespace": "prod",
          "summary": { "create": 1, "update": 1, "delete": 0, "no_change": 2, "errors": 0 },
          "changes": [
            { "resource": "job", "key": "new-job", "action": "create", "diff": null },
            { "resource": "job", "key": "weekly-digest", "action": "update",
              "diff": [ { "field": "url", "from": "https://example.com", "to": "https://changed.com" } ] },
            { "resource": "channel", "key": "oncall", "action": "no_change", "diff": null }
          ],
          "errors": [],
          "applied": false
        }
        """;

        var plan = JsonSerializer.Deserialize<ReconcileResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(plan);
        Assert.False(plan!.Applied);
        Assert.Equal("prod", plan.Namespace);
        Assert.Equal(1, plan.Summary.Create);
        Assert.Equal(1, plan.Summary.Update);
        Assert.Equal(2, plan.Summary.NoChange);
        Assert.True(plan.HasWork);

        Assert.Equal(3, plan.Changes.Count);
        var update = Assert.Single(plan.Changes, c => c.Action == "update");
        Assert.Equal("weekly-digest", update.Key);
        Assert.NotNull(update.Diff);
        var diff = Assert.Single(update.Diff!);
        Assert.Equal("url", diff.Field);
        Assert.Equal("https://example.com", diff.From);
        Assert.Equal("https://changed.com", diff.To);
    }

    [Fact]
    public void ReconcileResponse_surfaces_plan_errors()
    {
        const string payload = """
        {
          "namespace": "prod",
          "summary": { "create": 0, "update": 0, "delete": 0, "no_change": 0, "errors": 1 },
          "changes": [],
          "errors": [
            { "resource": "job", "key": "job-5", "code": "plan_job_limit_exceeded", "message": "Too many jobs." }
          ],
          "applied": false
        }
        """;

        var plan = JsonSerializer.Deserialize<ReconcileResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(plan);
        Assert.False(plan!.HasWork);
        var error = Assert.Single(plan.Errors);
        Assert.Equal("plan_job_limit_exceeded", error.Code);
        Assert.Equal("job-5", error.Key);
    }
}
