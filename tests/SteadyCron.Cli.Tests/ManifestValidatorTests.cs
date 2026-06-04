using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ManifestValidatorTests
{
    private readonly ManifestValidator _validator = new();

    private ManifestValidator.ValidationResult Validate(string yaml) =>
        _validator.Validate(new ManifestLoader().Parse(yaml));

    // ── Valid manifests ───────────────────────────────────────────────────────────

    [Fact]
    public void Examples_manifest_is_valid()
    {
        const string yaml = """
        version: 2
        namespace: prod
        jobs:
          - name: weekly-digest-email
            kind: http
            method: POST
            url: https://api.myapp.com/jobs/digest
            schedule: "0 9 * * 1"
            timezone: Europe/Berlin
            timeout: 120
            retries: 3
          - name: nightly-db-backup
            kind: heartbeat
            schedule: "0 2 * * *"
            grace: 1800
        """;

        var result = Validate(yaml);
        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    // ── Job name validation ───────────────────────────────────────────────────────

    [Fact]
    public void Error_when_job_name_missing()
    {
        var result = Validate("jobs:\n  - kind: heartbeat\n    interval: 300");
        Assert.Contains(result.Errors, e => e.Contains("'name' is required"));
    }

    [Fact]
    public void Error_on_duplicate_job_names()
    {
        const string yaml = """
        jobs:
          - name: same
            kind: heartbeat
            interval: 300
          - name: same
            kind: heartbeat
            interval: 300
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("duplicate job name 'same'"));
    }

    // ── Schedule validation ────────────────────────────────────────────────────────

    [Fact]
    public void Error_when_no_schedule()
    {
        var result = Validate("jobs:\n  - name: x\n    kind: heartbeat");
        Assert.Contains(result.Errors, e => e.Contains("schedule is required"));
    }

    [Fact]
    public void Error_when_both_schedule_and_interval()
    {
        const string yaml = """
        jobs:
          - name: x
            kind: heartbeat
            schedule: "0 * * * *"
            interval: 300
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("either 'schedule'") || e.Contains("not both"));
    }

    [Fact]
    public void Error_on_invalid_cron_expression()
    {
        const string yaml = """
        jobs:
          - name: x
            kind: http
            url: https://example.com
            schedule: "not a cron"
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("5-field cron"));
    }

    [Fact]
    public void Error_on_cron_with_wrong_field_count()
    {
        const string yaml = """
        jobs:
          - name: x
            kind: http
            url: https://example.com
            schedule: "0 * * *"
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("5-field"));
    }

    [Fact]
    public void Valid_cron_expressions_pass()
    {
        foreach (var expr in new[] { "0 9 * * 1", "*/5 * * * *", "0 0 1 1 0", "? * * * *", "30 14 L * *" })
        {
            const string template = """
            jobs:
              - name: x
                kind: http
                url: https://example.com
                schedule: "{0}"
            """;

            var result = Validate(string.Format(template, expr));
            Assert.True(result.IsValid || !result.Errors.Any(e => e.Contains("cron")),
                $"Cron '{expr}' should be valid. Errors: {string.Join(", ", result.Errors)}");
        }
    }

    [Fact]
    public void Error_on_interval_out_of_range()
    {
        var result = Validate("jobs:\n  - name: x\n    kind: heartbeat\n    interval: 5");
        Assert.Contains(result.Errors, e => e.Contains("'interval' must be between"));
    }

    // ── HTTP job validation ────────────────────────────────────────────────────────

    [Fact]
    public void Error_when_http_job_missing_url()
    {
        var result = Validate("jobs:\n  - name: x\n    kind: http\n    interval: 300");
        Assert.Contains(result.Errors, e => e.Contains("'url' is required"));
    }

    [Fact]
    public void Error_when_heartbeat_fields_on_http_job()
    {
        const string yaml = """
        jobs:
          - name: x
            kind: http
            url: https://example.com
            interval: 300
            grace: 60
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("not valid for HTTP jobs") && e.Contains("grace"));
    }

    // ── Heartbeat job validation ───────────────────────────────────────────────────

    [Fact]
    public void Error_when_http_fields_on_heartbeat_job()
    {
        const string yaml = """
        jobs:
          - name: x
            kind: heartbeat
            interval: 300
            url: https://example.com
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("not valid for heartbeat jobs") && e.Contains("url"));
    }

    // ── Cross-reference validation ─────────────────────────────────────────────────

    [Fact]
    public void Error_when_job_tag_not_declared()
    {
        const string yaml = """
        version: 2
        tags:
          - key: env
            value: prod
        jobs:
          - name: x
            kind: heartbeat
            interval: 300
            tags: ["env:staging"]
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("env:staging") && e.Contains("not declared"));
    }

    [Fact]
    public void No_tag_error_when_tags_section_absent()
    {
        const string yaml = """
        version: 2
        jobs:
          - name: x
            kind: heartbeat
            interval: 300
            tags: ["env:prod"]
        """;

        var result = Validate(yaml);
        Assert.DoesNotContain(result.Errors, e => e.Contains("not declared"));
    }

    [Fact]
    public void Error_when_rule_channel_not_declared()
    {
        const string yaml = """
        version: 2
        channels:
          - id: ch1
            name: Slack
            kind: slack
        jobs:
          - name: x
            kind: heartbeat
            interval: 300
            rules:
              - channel: unknown-channel
                trigger: on_failure
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("unknown-channel") && e.Contains("not declared"));
    }

    [Fact]
    public void Rule_channel_resolved_by_name()
    {
        const string yaml = """
        version: 2
        channels:
          - name: "Slack #alerts"
            kind: slack
        jobs:
          - name: x
            kind: heartbeat
            interval: 300
            rules:
              - channel: "Slack #alerts"
                trigger: on_failure
        """;

        var result = Validate(yaml);
        Assert.DoesNotContain(result.Errors, e => e.Contains("not declared"));
    }

    // ── Channel validation ────────────────────────────────────────────────────────

    [Fact]
    public void Error_when_channel_missing_name()
    {
        var result = Validate("version: 2\nchannels:\n  - kind: slack\njobs: []");
        Assert.Contains(result.Errors, e => e.Contains("'name' is required"));
    }

    [Fact]
    public void Error_on_duplicate_channel_ids()
    {
        const string yaml = """
        version: 2
        channels:
          - id: same
            name: A
            kind: slack
          - id: same
            name: B
            kind: slack
        jobs: []
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("duplicate channel id 'same'"));
    }

    // ── Tag validation ────────────────────────────────────────────────────────────

    [Fact]
    public void Error_when_tag_missing_key()
    {
        var result = Validate("version: 2\ntags:\n  - value: prod\njobs: []");
        Assert.Contains(result.Errors, e => e.Contains("'key' is required"));
    }

    [Fact]
    public void Error_on_duplicate_tag_ids()
    {
        const string yaml = """
        version: 2
        tags:
          - id: t1
            key: env
            value: prod
          - id: t1
            key: env
            value: staging
        jobs: []
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("duplicate tag id 't1'"));
    }

    // ── Variable validation ───────────────────────────────────────────────────────

    [Fact]
    public void Error_when_variable_missing_name()
    {
        var result = Validate("version: 2\nvariables:\n  - value: foo\njobs: []");
        Assert.Contains(result.Errors, e => e.Contains("'name' is required"));
    }

    [Fact]
    public void Error_on_duplicate_variable_names()
    {
        const string yaml = """
        version: 2
        variables:
          - name: token
            value: abc
          - name: token
            value: xyz
        jobs: []
        """;

        var result = Validate(yaml);
        Assert.Contains(result.Errors, e => e.Contains("duplicate variable name 'token'"));
    }

    // ── Multiple errors accumulated ────────────────────────────────────────────────

    [Fact]
    public void Multiple_errors_all_reported()
    {
        const string yaml = """
        jobs:
          - kind: http
          - name: also-no-schedule
            kind: http
            url: https://example.com
        """;

        var result = Validate(yaml);
        Assert.True(result.Errors.Count >= 2);
    }
}
