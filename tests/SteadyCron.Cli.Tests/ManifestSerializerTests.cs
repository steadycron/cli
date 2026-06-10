using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ManifestSerializerTests
{
    private readonly ManifestLoader _loader = new();

    // ── Key order ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Key_order_follows_canonical_v2_sequence()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Namespace = "prod",
            Channels = [new ManifestChannel { Id = "ch1", Name = "Slack", Kind = "slack" }],
            Tags = [new ManifestTag { Id = "t1", Key = "env", Value = "prod" }],
            Variables = [new ManifestVariable { Id = "v1", Name = "token" }],
            Jobs = [new ManifestJob { Id = "job1", Name = "my-job", Kind = "heartbeat", Schedule = "0 * * * *" }],
        };

        var yaml = ManifestSerializer.Serialize(manifest);

        var versionPos = yaml.IndexOf("version:", StringComparison.Ordinal);
        var namespacePos = yaml.IndexOf("namespace:", StringComparison.Ordinal);
        var channelsPos = yaml.IndexOf("channels:", StringComparison.Ordinal);
        var tagsPos = yaml.IndexOf("tags:", StringComparison.Ordinal);
        var variablesPos = yaml.IndexOf("variables:", StringComparison.Ordinal);
        var jobsPos = yaml.IndexOf("jobs:", StringComparison.Ordinal);

        Assert.True(versionPos < namespacePos);
        Assert.True(namespacePos < channelsPos);
        Assert.True(channelsPos < tagsPos);
        Assert.True(tagsPos < variablesPos);
        Assert.True(variablesPos < jobsPos);
    }

    // ── Null omission ─────────────────────────────────────────────────────────────

    [Fact]
    public void Null_fields_are_omitted()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs =
            [
                new ManifestJob
                {
                    Id = "j1",
                    Name = "heartbeat-job",
                    Kind = "heartbeat",
                    Schedule = "0 2 * * *",
                    // Url, Method, Headers, Body, etc. all null
                },
            ],
        };

        var yaml = ManifestSerializer.Serialize(manifest);

        Assert.DoesNotContain("url:", yaml);
        Assert.DoesNotContain("method:", yaml);
        Assert.DoesNotContain("headers:", yaml);
        Assert.DoesNotContain("body:", yaml);
        Assert.DoesNotContain("namespace:", yaml);
        Assert.DoesNotContain("channels:", yaml);
        Assert.DoesNotContain("tags:", yaml);
        Assert.DoesNotContain("variables:", yaml);
    }

    // ── Round-trip through ManifestLoader ─────────────────────────────────────────

    [Fact]
    public void Serialized_http_job_round_trips_through_loader()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Namespace = "test",
            Jobs =
            [
                new ManifestJob
                {
                    Id = "weekly-report",
                    Name = "weekly-report",
                    Kind = "http",
                    Method = "POST",
                    Url = "https://app.example.com/api/report",
                    Schedule = "0 9 * * 1",
                    Timezone = "UTC",
                    Headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer token123",
                        ["Content-Type"] = "application/json",
                    },
                    Body = "{\"type\":\"weekly\"}",
                },
            ],
        };

        var yaml = ManifestSerializer.Serialize(manifest);
        var parsed = _loader.Parse(yaml);

        Assert.Equal(2, parsed.Version);
        Assert.Equal("test", parsed.Namespace);
        Assert.Single(parsed.Jobs!);

        var job = parsed.Jobs![0];
        Assert.Equal("weekly-report", job.Id);
        Assert.Equal("http", job.Kind);
        Assert.Equal("POST", job.Method);
        Assert.Equal("https://app.example.com/api/report", job.Url);
        Assert.Equal("0 9 * * 1", job.Schedule);
        Assert.Equal("UTC", job.Timezone);
        Assert.Equal("Bearer token123", job.Headers!["Authorization"]);
        Assert.Equal("{\"type\":\"weekly\"}", job.Body);
    }

    [Fact]
    public void Serialized_heartbeat_job_round_trips_through_loader()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs =
            [
                new ManifestJob
                {
                    Id = "nightly-backup",
                    Name = "nightly-backup",
                    Kind = "heartbeat",
                    Schedule = "0 2 * * *",
                    Timezone = "UTC",
                    Grace = 1800,
                },
            ],
        };

        var yaml = ManifestSerializer.Serialize(manifest);
        var parsed = _loader.Parse(yaml);
        var job = parsed.Jobs![0];

        Assert.Equal("heartbeat", job.Kind);
        Assert.Equal("0 2 * * *", job.Schedule);
        Assert.Equal(1800, job.Grace);
    }

    [Fact]
    public void Serialized_manifest_with_all_top_level_sections_round_trips()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Namespace = "prod",
            Channels =
            [
                new ManifestChannel
                {
                    Id = "slack",
                    Name = "Slack #alerts",
                    Kind = "slack",
                    Config = new Dictionary<string, string> { ["webhook_url"] = "https://hooks.slack.com/x" },
                },
            ],
            Tags = [new ManifestTag { Id = "env-prod", Key = "env", Value = "prod" }],
            Variables = [new ManifestVariable { Id = "tok", Name = "api_token" }],
            Jobs =
            [
                new ManifestJob
                {
                    Id = "job1",
                    Name = "job1",
                    Kind = "http",
                    Url = "https://example.com",
                    Schedule = "0 * * * *",
                },
            ],
        };

        var yaml = ManifestSerializer.Serialize(manifest);
        var parsed = _loader.Parse(yaml);

        Assert.Equal("prod", parsed.Namespace);
        Assert.Single(parsed.Channels!);
        Assert.Single(parsed.Tags!);
        Assert.Single(parsed.Variables!);
        Assert.Single(parsed.Jobs!);
    }

    // ── Required-env header ───────────────────────────────────────────────────────

    [Fact]
    public void Required_env_vars_are_prepended_as_comment_block()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs =
            [
                new ManifestJob
                {
                    Id = "j1",
                    Name = "j1",
                    Kind = "http",
                    Url = "${API_BASE}/endpoint",
                    Schedule = "0 * * * *",
                },
            ],
        };

        var yaml = ManifestSerializer.Serialize(manifest, ["API_BASE", "ANOTHER_VAR"]);

        Assert.StartsWith("# required env vars:", yaml);
        Assert.Contains("#   API_BASE", yaml);
        Assert.Contains("#   ANOTHER_VAR", yaml);
        // YAML content still present
        Assert.Contains("version:", yaml);
        Assert.Contains("jobs:", yaml);
    }

    [Fact]
    public void No_header_when_required_env_vars_is_empty()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs = [new ManifestJob { Id = "j1", Name = "j1", Kind = "heartbeat", Schedule = "0 * * * *" }],
        };

        var yaml = ManifestSerializer.Serialize(manifest, []);
        Assert.StartsWith("version:", yaml);
    }

    // ── Empty collections are excluded ────────────────────────────────────────────

    [Fact]
    public void Empty_collections_are_excluded_from_output()
    {
        var manifest = new ManifestFile
        {
            Version = 2,
            Channels = [],
            Tags = [],
            Variables = [],
            Jobs = [new ManifestJob { Id = "j1", Name = "j1", Kind = "heartbeat", Schedule = "0 * * * *" }],
        };

        var yaml = ManifestSerializer.Serialize(manifest);

        Assert.DoesNotContain("channels:", yaml);
        Assert.DoesNotContain("tags:", yaml);
        Assert.DoesNotContain("variables:", yaml);
    }
}
