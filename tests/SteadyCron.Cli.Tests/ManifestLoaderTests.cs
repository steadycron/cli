using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ManifestLoaderTests
{
    private readonly ManifestLoader _loader = new();

    [Fact]
    public void Parses_documented_manifest_shape()
    {
        const string yaml = """
        version: 1
        jobs:
          - name: weekly-digest-email
            kind: http
            method: POST
            url: https://api.myapp.com/jobs/digest
            schedule: "0 9 * * 1"
            timezone: Europe/Berlin
            timeout: 120
            retries: 3
            headers:
              Authorization: "Bearer {{token}}"
            retry_on_status: [500, 502, 503]

          - name: nightly-db-backup
            kind: heartbeat
            schedule: "0 2 * * *"
            grace: 1800
        """;

        var manifest = _loader.Parse(yaml);

        Assert.Equal(1, manifest.Version);
        Assert.NotNull(manifest.Jobs);
        Assert.Equal(2, manifest.Jobs!.Count);

        var http = manifest.Jobs[0];
        Assert.Equal("weekly-digest-email", http.Name);
        Assert.Equal("http", http.Kind);
        Assert.Equal("POST", http.Method);
        Assert.Equal("0 9 * * 1", http.Schedule);
        Assert.Equal(120, http.Timeout);
        Assert.Equal(3, http.Retries);
        Assert.Equal("Bearer {{token}}", http.Headers!["Authorization"]);
        Assert.Equal(new[] { 500, 502, 503 }, http.RetryOnStatus);

        var heartbeat = manifest.Jobs[1];
        Assert.Equal("heartbeat", heartbeat.Kind);
        Assert.Equal(1800, heartbeat.Grace);
    }

    [Fact]
    public void Empty_manifest_throws()
    {
        Assert.Throws<ManifestException>(() => _loader.Parse("   "));
    }

    [Fact]
    public void Invalid_yaml_throws_manifest_exception()
    {
        const string yaml = "jobs:\n  - name: x\n   bad-indent: true";
        Assert.Throws<ManifestException>(() => _loader.Parse(yaml));
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        const string yaml = """
        jobs:
          - name: x
            kind: heartbeat
            interval: 300
            totally_unknown_field: 42
        """;

        var manifest = _loader.Parse(yaml);
        Assert.Single(manifest.Jobs!);
        Assert.Equal(300, manifest.Jobs![0].Interval);
    }
}
