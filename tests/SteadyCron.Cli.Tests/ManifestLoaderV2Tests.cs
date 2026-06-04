using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ManifestLoaderV2Tests
{
    private readonly ManifestLoader _loader = new();

    // ── v2 manifest parsing ───────────────────────────────────────────────────────

    [Fact]
    public void Parses_v2_manifest_with_all_top_level_resources()
    {
        const string yaml = """
        version: 2
        namespace: prod

        channels:
          - id: slack-oncall
            name: "Slack #oncall"
            kind: slack
            config:
              webhook_url: https://hooks.slack.com/xxx

        tags:
          - id: env-prod
            key: env
            value: prod
            color: "#00ff00"

        variables:
          - id: digest-token
            name: digest_token
            value: secret123

        shaping:
          max_concurrent_jobs: 5

        jobs:
          - id: weekly-digest
            name: weekly-digest-email
            kind: http
            method: POST
            url: https://api.myapp.com/jobs/digest
            schedule: "0 9 * * 1"
            tags: ["env:prod"]
            rules:
              - channel: slack-oncall
                trigger: on_failure
                severity: p1
        """;

        var manifest = _loader.Parse(yaml);

        Assert.Equal(2, manifest.Version);
        Assert.Equal("prod", manifest.Namespace);

        Assert.Single(manifest.Channels!);
        Assert.Equal("slack-oncall", manifest.Channels![0].Id);
        Assert.Equal("Slack #oncall", manifest.Channels![0].Name);

        Assert.Single(manifest.Tags!);
        Assert.Equal("env-prod", manifest.Tags![0].Id);
        Assert.Equal("env", manifest.Tags![0].Key);

        Assert.Single(manifest.Variables!);
        Assert.Equal("digest-token", manifest.Variables![0].Id);

        Assert.NotNull(manifest.Shaping);
        Assert.Equal(5, manifest.Shaping!.MaxConcurrentJobs);

        Assert.Single(manifest.Jobs!);
        var job = manifest.Jobs![0];
        Assert.Equal("weekly-digest", job.Id);
        Assert.Contains("env:prod", job.Tags!);
        Assert.Single(job.Rules!);
        Assert.Equal("slack-oncall", job.Rules![0].Channel);
        Assert.Equal("on_failure", job.Rules![0].Trigger);
    }

    [Fact]
    public void Parses_omitted_version_as_null()
    {
        const string yaml = """
        jobs:
          - name: job-x
            kind: heartbeat
            interval: 300
        """;

        var manifest = _loader.Parse(yaml);
        Assert.Null(manifest.Version);
    }

    // ── v1 detection ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsV1_true_for_version_1()
    {
        var m = _loader.Parse("version: 1\njobs: []");
        Assert.True(ManifestLoader.IsV1(m));
    }

    [Fact]
    public void IsV1_false_for_version_2()
    {
        var m = _loader.Parse("version: 2\njobs: []");
        Assert.False(ManifestLoader.IsV1(m));
    }

    [Fact]
    public void IsV1_false_when_no_version()
    {
        var m = _loader.Parse("jobs: []");
        Assert.False(ManifestLoader.IsV1(m));
    }

    // ── env interpolation ─────────────────────────────────────────────────────────

    [Fact]
    public void Env_vars_are_interpolated_before_parse()
    {
        const string yaml = """
        version: 2
        jobs:
          - name: my-job
            kind: http
            url: ${API_URL}
            interval: 60
        """;

        var manifest = _loader.Parse(
            yaml,
            getVar: name => name == "API_URL" ? "https://resolved.example.com" : null);

        Assert.Equal("https://resolved.example.com", manifest.Jobs![0].Url);
    }

    [Fact]
    public void Missing_env_var_throws_manifest_exception()
    {
        const string yaml = "jobs:\n  - name: x\n    kind: http\n    url: ${MISSING}\n    interval: 60";
        Assert.Throws<ManifestException>(() =>
            _loader.Parse(yaml, getVar: _ => null));
    }

    // ── multi-file / directory merge ──────────────────────────────────────────────

    [Fact]
    public void LoadFromPaths_merges_multiple_files()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "channels.yaml", """
        version: 2
        namespace: prod
        channels:
          - id: ch1
            name: Slack
            kind: slack
        """);
        WriteFile(dir, "jobs.yaml", """
        version: 2
        namespace: prod
        jobs:
          - name: job-a
            kind: heartbeat
            interval: 300
        """);

        var manifest = _loader.LoadFromPaths([dir]);

        Assert.Single(manifest.Channels!);
        Assert.Single(manifest.Jobs!);
        Assert.Equal("prod", manifest.Namespace);
    }

    [Fact]
    public void LoadFromPaths_rejects_duplicate_resource_id()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "a.yaml", """
        version: 2
        jobs:
          - id: dup
            name: job-a
            kind: heartbeat
            interval: 300
        """);
        WriteFile(dir, "b.yaml", """
        version: 2
        jobs:
          - id: dup
            name: job-b
            kind: heartbeat
            interval: 300
        """);

        Assert.Throws<ManifestException>(() => _loader.LoadFromPaths([dir]));
    }

    [Fact]
    public void LoadFromPaths_rejects_conflicting_namespaces()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "a.yaml", "version: 2\nnamespace: prod\njobs: []");
        WriteFile(dir, "b.yaml", "version: 2\nnamespace: staging\njobs: []");

        Assert.Throws<ManifestException>(() => _loader.LoadFromPaths([dir]));
    }

    [Fact]
    public void LoadFromPaths_rejects_conflicting_versions()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "a.yaml", "version: 1\njobs: []");
        WriteFile(dir, "b.yaml", "version: 2\njobs: []");

        Assert.Throws<ManifestException>(() => _loader.LoadFromPaths([dir]));
    }

    [Fact]
    public void LoadFromPaths_throws_when_no_yaml_files_found()
    {
        var dir = CreateTempDir();
        Assert.Throws<ManifestException>(() => _loader.LoadFromPaths([dir]));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string dir, string name, string content) =>
        File.WriteAllText(Path.Combine(dir, name), content);
}
