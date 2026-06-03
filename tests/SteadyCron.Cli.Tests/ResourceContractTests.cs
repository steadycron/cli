using System.Text.Json;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Commands.Rules;
using SteadyCron.Cli.Output;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ResourceContractTests
{
    // ── trigger / severity parsing ────────────────────────────────────────────────

    [Theory]
    [InlineData("failure", AlertTrigger.OnFailure)]
    [InlineData("on_failure", AlertTrigger.OnFailure)]
    [InlineData("n_consecutive", AlertTrigger.OnNConsecutive)]
    [InlineData("missed", AlertTrigger.OnMissedHeartbeat)]
    [InlineData("missed-heartbeat", AlertTrigger.OnMissedHeartbeat)]
    [InlineData("recovery", AlertTrigger.OnRecovery)]
    [InlineData("slow", AlertTrigger.OnSlowRun)]
    [InlineData("on_slow_run", AlertTrigger.OnSlowRun)]
    [InlineData("size_anomaly", AlertTrigger.OnSizeAnomaly)]
    public void ParseTrigger_accepts_friendly_forms(string input, AlertTrigger expected)
    {
        Assert.Equal(expected, AlertParsing.ParseTrigger(input));
    }

    [Fact]
    public void ParseTrigger_rejects_unknown()
    {
        Assert.Throws<CliException>(() => AlertParsing.ParseTrigger("nope"));
    }

    [Theory]
    [InlineData("p1", AlertSeverity.P1)]
    [InlineData("P2", AlertSeverity.P2)]
    [InlineData("p3", AlertSeverity.P3)]
    public void ParseSeverity_is_case_insensitive(string input, AlertSeverity expected)
    {
        Assert.Equal(expected, AlertParsing.ParseSeverity(input));
    }

    [Fact]
    public void IsSmart_only_for_anomaly_triggers()
    {
        Assert.True(AlertParsing.IsSmart(AlertTrigger.OnSlowRun));
        Assert.True(AlertParsing.IsSmart(AlertTrigger.OnSizeAnomaly));
        Assert.False(AlertParsing.IsSmart(AlertTrigger.OnFailure));
    }

    // ── wire contracts ────────────────────────────────────────────────────────────

    [Fact]
    public void AlertRule_request_serializes_snake_case_trigger_and_severity()
    {
        var request = new CreateAlertRuleRequest
        {
            ChannelId = Guid.Parse("0192a4e0-0000-7000-8000-000000000001"),
            Trigger = AlertTrigger.OnNConsecutive,
            Severity = AlertSeverity.P1,
        };

        var json = JsonSerializer.Serialize(request, SteadyCronJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("on_n_consecutive", root.GetProperty("trigger").GetString());
        Assert.Equal("p1", root.GetProperty("severity").GetString());
        Assert.True(root.TryGetProperty("channel_id", out _));
        Assert.False(root.TryGetProperty("params", out _)); // null params omitted
    }

    [Fact]
    public void AlertRule_params_serialize_with_literal_snake_case_keys()
    {
        var request = new CreateAlertRuleRequest
        {
            ChannelId = Guid.NewGuid(),
            Trigger = AlertTrigger.OnSlowRun,
            Params = new Dictionary<string, object> { ["factor"] = 3.0, ["min_baseline_samples"] = 5 },
        };

        var json = JsonSerializer.Serialize(request, SteadyCronJson.Options);
        using var doc = JsonDocument.Parse(json);
        var prms = doc.RootElement.GetProperty("params");

        Assert.Equal(3.0, prms.GetProperty("factor").GetDouble());
        Assert.Equal(5, prms.GetProperty("min_baseline_samples").GetInt32());
    }

    [Fact]
    public void Channel_request_serializes_kind_and_config()
    {
        var request = new CreateAlertChannelRequest(
            "Ops email", "email", new Dictionary<string, string> { ["to"] = "ops@example.com" });

        var json = JsonSerializer.Serialize(request, SteadyCronJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("email", root.GetProperty("kind").GetString());
        Assert.Equal("ops@example.com", root.GetProperty("config").GetProperty("to").GetString());
    }

    [Fact]
    public void TagResponse_deserializes_snake_case_job_count()
    {
        const string payload = """
        {
          "id": "0192a4e0-0000-7000-8000-000000000001",
          "account_id": "0192a4e0-0000-7000-8000-0000000000aa",
          "key": "env",
          "value": "prod",
          "color": "green",
          "job_count": 4,
          "created_at": "2026-06-03T10:00:00+00:00"
        }
        """;

        var tag = JsonSerializer.Deserialize<TagResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(tag);
        Assert.Equal("env", tag!.Key);
        Assert.Equal(4, tag.JobCount);
        Assert.Equal("env:prod", tag.Display);
    }

    [Fact]
    public void AlertChannelResponse_deserializes_with_redacted_config()
    {
        const string payload = """
        {
          "id": "0192a4e0-0000-7000-8000-000000000002",
          "account_id": "0192a4e0-0000-7000-8000-0000000000aa",
          "name": "Slack",
          "kind": "slack",
          "config": { "webhook_url": "***" },
          "created_at": "2026-06-03T10:00:00+00:00"
        }
        """;

        var channel = JsonSerializer.Deserialize<AlertChannelResponse>(payload, SteadyCronJson.Options);

        Assert.NotNull(channel);
        Assert.Equal("slack", channel!.Kind);
        Assert.Equal("***", channel.Config!["webhook_url"]);
    }
}
