using SteadyCron.Cli.Commands.Import;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ImportCrontabTests
{
    private readonly ManifestLoader _loader = new();

    // ── Basic parsing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Bare_url_becomes_http_job()
    {
        var result = CrontabParser.Parse(
            ["0 9 * * 1  https://api.example.com/trigger"],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("http", job.Kind);
        Assert.Equal("GET", job.Method);
        Assert.Equal("https://api.example.com/trigger", job.Url);
        Assert.Equal("0 9 * * 1", job.Schedule);
        Assert.Equal("UTC", job.Timezone);
        Assert.Equal(1, result.HttpCount);
        Assert.Equal(0, result.HeartbeatCount);
    }

    [Fact]
    public void Non_http_command_becomes_heartbeat()
    {
        var result = CrontabParser.Parse(
            ["0 2 * * * /usr/bin/php /var/www/scripts/backup.php"],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("heartbeat", job.Kind);
        Assert.Null(job.Url);
        Assert.Equal("0 2 * * *", job.Schedule);
        Assert.Equal(0, result.HttpCount);
        Assert.Equal(1, result.HeartbeatCount);
    }

    [Fact]
    public void Comment_above_entry_becomes_job_name()
    {
        var result = CrontabParser.Parse(
            [
                "# Send weekly digest",
                "0 9 * * 1  https://api.example.com/digest",
            ],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("Send weekly digest", job.Name);
        Assert.Equal("send-weekly-digest", job.Id);
    }

    [Fact]
    public void Blank_line_resets_pending_comment()
    {
        var result = CrontabParser.Parse(
            [
                "# This comment is orphaned",
                "",
                "0 9 * * 1  https://api.example.com/digest",
            ],
            isSystem: false);

        Assert.Single(result.Jobs);
        // Name should NOT be "This comment is orphaned" — blank line cleared it
        Assert.NotEqual("This comment is orphaned", result.Jobs[0].Name);
    }

    [Fact]
    public void Env_assignment_lines_are_skipped()
    {
        var result = CrontabParser.Parse(
            [
                "MAILTO=\"\"",
                "PATH=/usr/bin:/bin",
                "0 9 * * 1  https://api.example.com/trigger",
            ],
            isSystem: false);

        Assert.Single(result.Jobs);
        Assert.Equal(0, result.SkippedCount);
    }

    // ── Curl parsing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Curl_plain_url_becomes_http_get()
    {
        var result = CrontabParser.Parse(
            ["*/5 * * * * curl -fsS https://api.example.com/health"],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("http", job.Kind);
        Assert.Equal("GET", job.Method);
        Assert.Equal("https://api.example.com/health", job.Url);
    }

    [Fact]
    public void Curl_with_method_headers_and_body()
    {
        var result = CrontabParser.Parse(
            ["0 8 * * * curl https://api.example.com/sync -X POST -H 'Authorization: Bearer tok' -H 'Content-Type: application/json' -d '{\"k\":\"v\"}'"],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("http", job.Kind);
        Assert.Equal("POST", job.Method);
        Assert.Equal("https://api.example.com/sync", job.Url);
        Assert.Equal("Bearer tok", job.Headers!["Authorization"]);
        Assert.Equal("application/json", job.Headers["Content-Type"]);
        Assert.Equal("{\"k\":\"v\"}", job.Body);
    }

    [Fact]
    public void Curl_with_double_quoted_header()
    {
        var result = CrontabParser.Parse(
            ["0 * * * * curl -H \"X-Secret: mysecret\" https://api.example.com/endpoint"],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("mysecret", job.Headers!["X-Secret"]);
    }

    [Fact]
    public void Curl_data_implies_POST_method()
    {
        var result = CrontabParser.Parse(
            ["0 * * * * curl -d '{\"x\":1}' https://api.example.com/post"],
            isSystem: false);

        var job = result.Jobs[0];
        Assert.Equal("POST", job.Method);
    }

    [Fact]
    public void Wget_bare_url_becomes_http_get()
    {
        var result = CrontabParser.Parse(
            ["30 6 * * * wget -q -O /dev/null https://example.com/task"],
            isSystem: false);

        Assert.Single(result.Jobs);
        var job = result.Jobs[0];
        Assert.Equal("http", job.Kind);
        Assert.Equal("GET", job.Method);
        Assert.Equal("https://example.com/task", job.Url);
    }

    // ── Macros ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("@yearly",    "0 0 1 1 *")]
    [InlineData("@annually",  "0 0 1 1 *")]
    [InlineData("@monthly",   "0 0 1 * *")]
    [InlineData("@weekly",    "0 0 * * 0")]
    [InlineData("@daily",     "0 0 * * *")]
    [InlineData("@midnight",  "0 0 * * *")]
    [InlineData("@hourly",    "0 * * * *")]
    public void Macro_expands_to_correct_cron_expression(string macro, string expected)
    {
        var result = CrontabParser.Parse(
            [$"{macro} /usr/bin/php /var/www/script.php"],
            isSystem: false);

        Assert.Single(result.Jobs);
        Assert.Equal(expected, result.Jobs[0].Schedule);
    }

    [Fact]
    public void Reboot_is_skipped_with_warning()
    {
        var result = CrontabParser.Parse(
            ["@reboot /usr/bin/start_service.sh"],
            isSystem: false);

        Assert.Empty(result.Jobs);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(result.Warnings);
        Assert.Contains("@reboot", result.Warnings[0]);
    }

    // ── System crontab ────────────────────────────────────────────────────────────

    [Fact]
    public void System_crontab_format_strips_username_column()
    {
        var result = CrontabParser.Parse(
            ["0 3 * * * root /usr/bin/php /var/www/nightly.php"],
            isSystem: true);

        Assert.Single(result.Jobs);
        // command is "/usr/bin/php /var/www/nightly.php", not "root ..."
        // (php is not curl/wget so it's a heartbeat — but schedule should be correct)
        Assert.Equal("0 3 * * *", result.Jobs[0].Schedule);
        Assert.Equal("heartbeat", result.Jobs[0].Kind);
    }

    [Fact]
    public void System_crontab_curl_command_becomes_http()
    {
        var result = CrontabParser.Parse(
            ["*/15 * * * * www-data curl -fsS https://api.example.com/ping"],
            isSystem: true);

        Assert.Single(result.Jobs);
        Assert.Equal("http", result.Jobs[0].Kind);
        Assert.Equal("https://api.example.com/ping", result.Jobs[0].Url);
    }

    // ── --as flag overrides ───────────────────────────────────────────────────────

    [Fact]
    public void Force_heartbeat_overrides_curl_classification()
    {
        var result = CrontabParser.Parse(
            ["0 * * * * curl https://api.example.com/ping"],
            isSystem: false,
            forceAs: "heartbeat");

        Assert.Single(result.Jobs);
        Assert.Equal("heartbeat", result.Jobs[0].Kind);
    }

    [Fact]
    public void Force_http_on_non_curl_skips_if_no_url()
    {
        var result = CrontabParser.Parse(
            ["0 * * * * /usr/bin/php script.php"],
            isSystem: false,
            forceAs: "http");

        Assert.Empty(result.Jobs);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(result.Warnings);
    }

    // ── Duplicate ids ─────────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_ids_get_numeric_suffix()
    {
        var result = CrontabParser.Parse(
            [
                "# My job",
                "0 * * * * https://api.example.com/a",
                "# My job",
                "0 * * * * https://api.example.com/b",
            ],
            isSystem: false);

        Assert.Equal(2, result.Jobs.Count);
        var ids = result.Jobs.Select(j => j.Id).ToList();
        Assert.Distinct(ids);
        Assert.Contains("my-job", ids);
        Assert.Contains("my-job-2", ids);
    }

    // ── Unparseable lines ─────────────────────────────────────────────────────────

    [Fact]
    public void Incomplete_line_is_skipped_with_warning()
    {
        var result = CrontabParser.Parse(
            ["* * * *"],  // only 4 fields
            isSystem: false);

        Assert.Empty(result.Jobs);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Empty_input_returns_empty_result()
    {
        var result = CrontabParser.Parse([], isSystem: false);
        Assert.Empty(result.Jobs);
        Assert.Empty(result.Warnings);
    }

    // ── Round-trip through ManifestLoader ─────────────────────────────────────────

    [Fact]
    public void Mixed_crontab_output_round_trips_through_manifest_loader()
    {
        var result = CrontabParser.Parse(
            [
                "# Weekly digest",
                "0 9 * * 1  curl https://api.example.com/digest -X POST",
                "# Nightly backup",
                "0 2 * * *  /usr/bin/pg_dump --clean mydb | gzip > /backups/db.sql.gz",
            ],
            isSystem: false);

        var manifest = new ManifestFile
        {
            Version = 2,
            Jobs = [.. result.Jobs],
        };

        var yaml = ManifestSerializer.Serialize(manifest);
        var parsed = _loader.Parse(yaml, getVar: _ => null);

        Assert.Equal(2, parsed.Jobs!.Count);
        Assert.Contains(parsed.Jobs, j => j.Kind == "http");
        Assert.Contains(parsed.Jobs, j => j.Kind == "heartbeat");
    }

    // ── Shell tokenizer ───────────────────────────────────────────────────────────

    [Fact]
    public void TokenizeShell_handles_single_quotes()
    {
        var tokens = CrontabParser.TokenizeShell("curl -H 'X-Key: value with spaces' https://example.com");
        Assert.Equal(["curl", "-H", "X-Key: value with spaces", "https://example.com"], tokens);
    }

    [Fact]
    public void TokenizeShell_handles_double_quotes()
    {
        var tokens = CrontabParser.TokenizeShell("curl -H \"Authorization: Bearer tok\" https://example.com");
        Assert.Equal(["curl", "-H", "Authorization: Bearer tok", "https://example.com"], tokens);
    }

    [Fact]
    public void TokenizeShell_handles_backslash_escape()
    {
        var tokens = CrontabParser.TokenizeShell(@"curl https://example.com\ api");
        Assert.Equal(["curl", "https://example.com api"], tokens);
    }
}
