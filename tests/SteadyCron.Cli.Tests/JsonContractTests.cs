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
    public void CronPreviewResponse_maps_next_fires()
    {
        const string payload = """
        { "next_fires": ["2026-06-03T09:00:00+00:00", "2026-06-10T09:00:00+00:00"] }
        """;

        var response = JsonSerializer.Deserialize<CronPreviewResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(response);
        Assert.Equal(2, response!.NextFires.Count);
    }
}
