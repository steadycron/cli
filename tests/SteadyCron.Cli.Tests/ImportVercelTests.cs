using SteadyCron.Cli.Commands.Import;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ImportVercelTests
{
    private readonly ManifestLoader _loader = new();

    // ── Basic parsing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Basic_vercel_json_produces_http_jobs_with_correct_urls()
    {
        const string json = """
        {
          "crons": [
            { "path": "/api/weekly-report", "schedule": "0 9 * * 1" },
            { "path": "/api/daily-summary", "schedule": "0 6 * * *" }
          ]
        }
        """;

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Equal(2, result.Jobs.Count);

        var weekly = result.Jobs[0];
        Assert.Equal("http", weekly.Kind);
        Assert.Equal("GET", weekly.Method);
        Assert.Equal("https://app.example.com/api/weekly-report", weekly.Url);
        Assert.Equal("0 9 * * 1", weekly.Schedule);
        Assert.Equal("UTC", weekly.Timezone);
        Assert.Null(weekly.Headers);

        var daily = result.Jobs[1];
        Assert.Equal("https://app.example.com/api/daily-summary", daily.Url);
        Assert.Equal("0 6 * * *", daily.Schedule);
    }

    [Fact]
    public void Base_url_trailing_slash_is_normalized()
    {
        const string json = """{"crons":[{"path":"/api/trigger","schedule":"0 * * * *"}]}""";

        var result = VercelParser.Parse(json, "https://app.example.com/");

        Assert.Equal("https://app.example.com/api/trigger", result.Jobs[0].Url);
    }

    [Fact]
    public void Path_without_leading_slash_is_handled()
    {
        const string json = """{"crons":[{"path":"api/trigger","schedule":"0 * * * *"}]}""";

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Equal("https://app.example.com/api/trigger", result.Jobs[0].Url);
    }

    // ── manifest_key from path ─────────────────────────────────────────────────────

    [Fact]
    public void Manifest_key_is_slug_of_path()
    {
        const string json = """{"crons":[{"path":"/api/weekly-report","schedule":"0 * * * *"}]}""";

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Equal("api-weekly-report", result.Jobs[0].Id);
    }

    [Fact]
    public void Duplicate_paths_get_deduplicated_ids()
    {
        const string json = """
        {
          "crons": [
            {"path":"/api/job","schedule":"0 * * * *"},
            {"path":"/api/job","schedule":"0 1 * * *"}
          ]
        }
        """;

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Equal(2, result.Jobs.Count);
        var ids = result.Jobs.Select(j => j.Id).ToList();
        Assert.Distinct(ids);
    }

    // ── --cron-secret-env ─────────────────────────────────────────────────────────

    [Fact]
    public void Cron_secret_env_adds_authorization_header()
    {
        const string json = """{"crons":[{"path":"/api/cron","schedule":"0 * * * *"}]}""";

        var result = VercelParser.Parse(json, "https://app.example.com", "VERCEL_CRON_SECRET");

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.NotNull(job.Headers);
        Assert.Equal("Bearer ${VERCEL_CRON_SECRET}", job.Headers!["Authorization"]);
    }

    [Fact]
    public void Cron_secret_env_placeholder_appears_in_serialized_manifest()
    {
        const string json = """{"crons":[{"path":"/api/cron","schedule":"0 * * * *"}]}""";

        var result = VercelParser.Parse(json, "https://app.example.com", "VERCEL_CRON_SECRET");
        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs = [.. result.Jobs],
        };

        var yaml = ManifestSerializer.Serialize(manifest);

        // The placeholder is NOT resolved — it stays as a literal for sync to interpolate
        Assert.Contains("${VERCEL_CRON_SECRET}", yaml);
        // EnvInterpolator.FindPlaceholders should locate it
        var placeholders = EnvInterpolator.FindPlaceholders(yaml);
        Assert.Contains("VERCEL_CRON_SECRET", placeholders);
    }

    [Fact]
    public void No_cron_secret_env_means_no_headers()
    {
        const string json = """{"crons":[{"path":"/api/cron","schedule":"0 * * * *"}]}""";

        var result = VercelParser.Parse(json, "https://app.example.com", cronSecretEnv: null);

        Assert.Null(result.Jobs[0].Headers);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_crons_array_produces_warning()
    {
        const string json = """{"crons":[]}""";

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Empty(result.Jobs);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Missing_crons_key_produces_warning()
    {
        const string json = """{"builds":[]}""";

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Empty(result.Jobs);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Cron_entry_missing_path_is_skipped_with_warning()
    {
        const string json = """
        {
          "crons": [
            {"schedule":"0 * * * *"},
            {"path":"/api/ok","schedule":"0 * * * *"}
          ]
        }
        """;

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.Single(result.Jobs);
        Assert.Single(result.Warnings);
        Assert.Contains("path", result.Warnings[0]);
    }

    [Fact]
    public void Invalid_json_returns_error_in_warnings()
    {
        var result = VercelParser.Parse("not json at all", "https://app.example.com");

        Assert.Empty(result.Jobs);
        Assert.Single(result.Warnings);
        Assert.Contains("parse", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    // ── Round-trip through ManifestLoader ─────────────────────────────────────────

    [Fact]
    public void Vercel_import_output_round_trips_through_manifest_loader()
    {
        const string json = """
        {
          "crons": [
            { "path": "/api/weekly-report",  "schedule": "0 9 * * 1" },
            { "path": "/api/health-check",   "schedule": "*/5 * * * *" }
          ]
        }
        """;

        var result = VercelParser.Parse(json, "https://app.example.com", "MY_SECRET");

        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs = [.. result.Jobs],
        };

        var yaml = ManifestSerializer.Serialize(manifest);

        // Parse back — env vars will be unresolved but loader should accept them since
        // we are not calling Apply() here (we test with getVar: _ => null which skips
        // interpolation errors by providing a passthrough).
        // Instead we test that the YAML is structurally valid by checking it parses
        // when the env var is supplied.
        var parsed = _loader.Parse(yaml, getVar: name => name == "MY_SECRET" ? "resolved" : null);

        Assert.Equal(2, parsed.Jobs!.Count);
        Assert.All(parsed.Jobs, j =>
        {
            Assert.Equal("http", j.Kind);
            Assert.Equal("GET", j.Method);
            Assert.Equal("UTC", j.Timezone);
        });
    }

    [Fact]
    public void All_jobs_have_timezone_utc()
    {
        const string json = """
        {
          "crons": [
            {"path":"/a","schedule":"0 * * * *"},
            {"path":"/b","schedule":"30 * * * *"}
          ]
        }
        """;

        var result = VercelParser.Parse(json, "https://app.example.com");

        Assert.All(result.Jobs, j => Assert.Equal("UTC", j.Timezone));
    }
}
