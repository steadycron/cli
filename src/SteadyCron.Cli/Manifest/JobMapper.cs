using System.Globalization;
using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Manifest;

/// <summary>The outcome of diffing a desired job against its server counterpart.</summary>
public sealed record UpdateResult(
    bool HasChanges,
    UpdateJobRequest Request,
    IReadOnlyList<FieldChange> Changes);

/// <summary>
/// Translates the friendly manifest schema into the API contract: normalizing/validating a
/// <see cref="ManifestJob"/> into a <see cref="DesiredJob"/>, producing create payloads, and
/// diffing desired vs. server state into a minimal <see cref="UpdateJobRequest"/>.
/// </summary>
public sealed class JobMapper
{
    private static readonly IReadOnlyList<string> HttpMethods = ManifestSchema.HttpMethods;

    // ── manifest → desired ───────────────────────────────────────────────────────

    public DesiredJob ToDesired(ManifestJob job, int index)
    {
        var label = string.IsNullOrWhiteSpace(job.Name) ? $"jobs[{index}]" : $"job '{job.Name.Trim()}'";

        if (string.IsNullOrWhiteSpace(job.Name))
        {
            throw new ManifestException($"{label}: 'name' is required.");
        }

        var name = job.Name.Trim();
        var kind = NormalizeKind(job.Kind, label);

        var (scheduleKind, cron, interval) = ResolveSchedule(job, label);

        var description = Empty(job.Description);
        var runbookNotes = Empty(job.RunbookNotes);
        var runbookUrl = Empty(job.RunbookUrl);
        var timezone = string.IsNullOrWhiteSpace(job.Timezone) ? JobDefaults.Timezone : job.Timezone.Trim();
        var paused = job.Paused ?? false;

        if (kind == "heartbeat")
        {
            RejectHttpFields(job, label);

            var grace = job.Grace ?? JobDefaults.GraceSeconds;
            if (grace is < 0 or > 86_400)
            {
                throw new ManifestException($"{label}: 'grace' must be between 0 and 86400 seconds.");
            }

            int? maxRunDuration = null;
            if (job.MaxRunDuration is { } mrd)
            {
                if (mrd is < 60 or > 86_400)
                {
                    throw new ManifestException($"{label}: 'max_run_duration' must be between 60 and 86400 seconds.");
                }

                maxRunDuration = mrd;
            }

            return new DesiredJob
            {
                Name = name,
                Kind = kind,
                JobKey = string.IsNullOrWhiteSpace(job.Id) ? null : job.Id.Trim(),
                Description = description,
                RunbookNotes = runbookNotes,
                RunbookUrl = runbookUrl,
                ScheduleKind = scheduleKind,
                CronExpression = cron,
                IntervalSeconds = interval,
                Timezone = timezone,
                Paused = paused,
                GraceSeconds = grace,
                StuckRunDetection = job.StuckRunDetection ?? JobDefaults.StuckRunDetection,
                MaxRunDurationSeconds = maxRunDuration,
            };
        }

        // HTTP job
        if (string.IsNullOrWhiteSpace(job.Url))
        {
            throw new ManifestException($"{label}: 'url' is required for HTTP jobs.");
        }

        var method = NormalizeMethod(job.Method, label);
        var retries = job.Retries ?? JobDefaults.MaxRetries;
        if (retries < 0)
        {
            throw new ManifestException($"{label}: 'retries' cannot be negative.");
        }

        var backoff = job.RetryBackoff ?? JobDefaults.RetryBackoffSeconds;
        if (backoff < 0)
        {
            throw new ManifestException($"{label}: 'retry_backoff' cannot be negative.");
        }

        var timeout = job.Timeout ?? JobDefaults.TimeoutSeconds;
        if (timeout <= 0)
        {
            throw new ManifestException($"{label}: 'timeout' must be a positive number of seconds.");
        }

        var statusCodes = NormalizeStatusCodes(job.RetryOnStatus, label);

        return new DesiredJob
        {
            Name = name,
            Kind = kind,
            JobKey = string.IsNullOrWhiteSpace(job.Id) ? null : job.Id.Trim(),
            Description = description,
            RunbookNotes = runbookNotes,
            RunbookUrl = runbookUrl,
            ScheduleKind = scheduleKind,
            CronExpression = cron,
            IntervalSeconds = interval,
            Timezone = timezone,
            Paused = paused,
            HttpMethod = method,
            HttpUrl = job.Url.Trim(),
            HttpHeaders = job.Headers is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(job.Headers, StringComparer.Ordinal),
            HttpBody = Empty(job.Body),
            TimeoutSeconds = timeout,
            MaxRetries = retries,
            RetryBackoffSeconds = backoff,
            RetryOnTimeout = job.RetryOnTimeout ?? JobDefaults.RetryOnTimeout,
            RetryOnStatusCodes = statusCodes,
            SkipIfRunning = job.SkipIfRunning ?? false,
            MisfirePolicyValue = NormalizeMisfire(job.MisfirePolicy, label),
        };
    }

    // ── desired → create ─────────────────────────────────────────────────────────

    public CreateJobRequest ToCreateRequest(DesiredJob d)
    {
        var status = d.Paused ? (ExecutionStatus?)ExecutionStatus.Paused : null;

        if (!d.IsHttp)
        {
            return new CreateJobRequest
            {
                Name = d.Name,
                Kind = "heartbeat",
                JobKey = d.JobKey,
                Description = d.Description,
                RunbookNotes = d.RunbookNotes,
                RunbookUrl = d.RunbookUrl,
                ScheduleKind = d.ScheduleKind,
                CronExpression = d.CronExpression,
                IntervalSeconds = d.IntervalSeconds,
                Timezone = d.Timezone,
                GraceSeconds = d.GraceSeconds,
                StuckRunDetection = d.StuckRunDetection,
                MaxRunDurationSeconds = d.MaxRunDurationSeconds,
                Status = status,
            };
        }

        return new CreateJobRequest
        {
            Name = d.Name,
            Kind = "http",
            JobKey = d.JobKey,
            Description = d.Description,
            RunbookNotes = d.RunbookNotes,
            RunbookUrl = d.RunbookUrl,
            ScheduleKind = d.ScheduleKind,
            CronExpression = d.CronExpression,
            IntervalSeconds = d.IntervalSeconds,
            Timezone = d.Timezone,
            HttpMethod = d.HttpMethod,
            HttpUrl = d.HttpUrl,
            HttpHeaders = d.HttpHeaders.Count > 0
                ? new Dictionary<string, string>(d.HttpHeaders, StringComparer.Ordinal)
                : null,
            HttpBody = d.HttpBody,
            TimeoutSeconds = d.TimeoutSeconds,
            MaxRetries = d.MaxRetries,
            RetryBackoffSeconds = d.RetryBackoffSeconds,
            RetryOnTimeout = d.RetryOnTimeout,
            RetryOnStatusCodes = d.RetryOnStatusCodes?.ToArray(),
            SkipIfRunning = d.SkipIfRunning,
            MisfirePolicy = ParseMisfireEnum(d.MisfirePolicyValue),
            Status = status,
        };
    }

    // ── desired vs server → update ────────────────────────────────────────────────

    public UpdateResult BuildUpdate(DesiredJob d, JobResponse server)
    {
        var changes = new List<FieldChange>();

        string? newDescription = null;
        if (!StringEquals(Empty(server.Description), d.Description))
        {
            newDescription = d.Description ?? string.Empty; // "" clears it server-side
            changes.Add(new FieldChange("description", Display(server.Description), Display(d.Description)));
        }

        string? newRunbookNotes = null;
        if (!StringEquals(Empty(server.RunbookNotes), d.RunbookNotes))
        {
            newRunbookNotes = d.RunbookNotes ?? string.Empty; // "" clears it server-side
            changes.Add(new FieldChange("runbook_notes", Display(server.RunbookNotes), Display(d.RunbookNotes)));
        }

        string? newRunbookUrl = null;
        if (!StringEquals(Empty(server.RunbookUrl), d.RunbookUrl))
        {
            newRunbookUrl = d.RunbookUrl ?? string.Empty; // "" clears it server-side
            changes.Add(new FieldChange("runbook_url", Display(server.RunbookUrl), Display(d.RunbookUrl)));
        }

        // ── schedule ──
        string? newCron = null;
        int? newInterval = null;
        var (serverScheduleKind, serverScheduleValue) = DescribeServerSchedule(server);
        var desiredScheduleValue = DescribeDesiredSchedule(d);
        if (!string.Equals(serverScheduleValue, desiredScheduleValue, StringComparison.Ordinal))
        {
            if (d.ScheduleKind == ScheduleKind.Cron)
            {
                newCron = d.CronExpression;
            }
            else
            {
                newInterval = d.IntervalSeconds;
            }

            changes.Add(new FieldChange("schedule", serverScheduleValue, desiredScheduleValue));
        }

        // ── timezone ──
        string? newTimezone = null;
        if (!string.Equals(server.Timezone, d.Timezone, StringComparison.Ordinal))
        {
            newTimezone = d.Timezone;
            changes.Add(new FieldChange("timezone", server.Timezone, d.Timezone));
        }

        var builder = new UpdateBuilder
        {
            Description = newDescription,
            RunbookNotes = newRunbookNotes,
            RunbookUrl = newRunbookUrl,
            CronExpression = newCron,
            IntervalSeconds = newInterval,
            Timezone = newTimezone,
        };

        if (d.IsHttp)
        {
            DiffHttp(d, server, changes, builder);
        }
        else
        {
            DiffHeartbeat(d, server, changes, builder);
        }

        return new UpdateResult(changes.Count > 0, builder.Build(), changes);
    }

    private void DiffHttp(DesiredJob d, JobResponse server, List<FieldChange> changes, UpdateBuilder b)
    {
        var serverMethod = server.HttpMethod?.ToUpperInvariant();
        if (!string.Equals(serverMethod, d.HttpMethod, StringComparison.Ordinal))
        {
            b.HttpMethod = d.HttpMethod;
            changes.Add(new FieldChange("method", Display(serverMethod), Display(d.HttpMethod)));
        }

        if (!string.Equals(server.HttpUrl, d.HttpUrl, StringComparison.Ordinal))
        {
            b.HttpUrl = d.HttpUrl;
            changes.Add(new FieldChange("url", Display(server.HttpUrl), Display(d.HttpUrl)));
        }

        if (!HeadersEqual(server.HttpHeaders, d.HttpHeaders))
        {
            // An empty dict (rather than null) clears headers server-side.
            b.HttpHeaders = new Dictionary<string, string>(d.HttpHeaders, StringComparer.Ordinal);
            changes.Add(new FieldChange("headers", DescribeHeaders(server.HttpHeaders), DescribeHeaders(d.HttpHeaders)));
        }

        if (!StringEquals(Empty(server.HttpBody), d.HttpBody))
        {
            b.HttpBody = d.HttpBody ?? string.Empty; // "" clears it server-side
            changes.Add(new FieldChange("body", Display(server.HttpBody), Display(d.HttpBody)));
        }

        var serverTimeout = server.TimeoutSeconds ?? JobDefaults.TimeoutSeconds;
        if (serverTimeout != d.TimeoutSeconds)
        {
            b.TimeoutSeconds = d.TimeoutSeconds;
            changes.Add(new FieldChange("timeout", serverTimeout.ToString(CultureInfo.InvariantCulture), d.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        var serverRetries = server.MaxRetries ?? JobDefaults.MaxRetries;
        if (serverRetries != d.MaxRetries)
        {
            b.MaxRetries = d.MaxRetries;
            changes.Add(new FieldChange("retries", serverRetries.ToString(CultureInfo.InvariantCulture), d.MaxRetries.ToString(CultureInfo.InvariantCulture)));
        }

        var serverBackoff = server.RetryBackoffSeconds ?? JobDefaults.RetryBackoffSeconds;
        if (serverBackoff != d.RetryBackoffSeconds)
        {
            b.RetryBackoffSeconds = d.RetryBackoffSeconds;
            changes.Add(new FieldChange("retry_backoff", serverBackoff.ToString(CultureInfo.InvariantCulture), d.RetryBackoffSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        // The API only applies retry_on_status_codes when retry_on_timeout is also present, so when
        // either differs we send both, and always carry the desired retry_on_timeout value.
        var retryTimeoutDiffers = server.RetryOnTimeout != d.RetryOnTimeout;
        var statusCodesDiffer = !StatusCodesEqual(server.RetryOnStatusCodes, d.RetryOnStatusCodes);
        if (retryTimeoutDiffers || statusCodesDiffer)
        {
            // retry_on_timeout must be present for the server to (re)apply the codes assignment.
            b.RetryOnTimeout = d.RetryOnTimeout;

            if (d.RetryOnStatusCodes is { Count: > 0 } desiredCodes)
            {
                b.RetryOnStatusCodes = desiredCodes.ToArray();
                b.RetryOnStatusCodesSet = true;
            }
            else
            {
                // Omit the field → the server sets codes to null = "retry on any non-2xx".
                b.RetryOnStatusCodesSet = false;
            }

            if (retryTimeoutDiffers)
            {
                changes.Add(new FieldChange("retry_on_timeout", server.RetryOnTimeout.ToString(), d.RetryOnTimeout.ToString()));
            }

            if (statusCodesDiffer)
            {
                changes.Add(new FieldChange("retry_on_status", DescribeCodes(server.RetryOnStatusCodes), DescribeCodes(d.RetryOnStatusCodes)));
            }
        }

        if (server.SkipIfRunning != d.SkipIfRunning)
        {
            b.SkipIfRunning = d.SkipIfRunning;
            changes.Add(new FieldChange("skip_if_running", server.SkipIfRunning.ToString(), d.SkipIfRunning.ToString()));
        }

        if (!string.Equals(server.MisfirePolicy, d.MisfirePolicyValue, StringComparison.Ordinal))
        {
            b.MisfirePolicy = ParseMisfireEnum(d.MisfirePolicyValue);
            changes.Add(new FieldChange("misfire_policy", server.MisfirePolicy, d.MisfirePolicyValue));
        }
    }

    private static void DiffHeartbeat(DesiredJob d, JobResponse server, List<FieldChange> changes, UpdateBuilder b)
    {
        if (server.GraceSeconds != d.GraceSeconds)
        {
            b.GraceSeconds = d.GraceSeconds;
            changes.Add(new FieldChange("grace", server.GraceSeconds.ToString(CultureInfo.InvariantCulture), d.GraceSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        if (server.StuckRunDetection != d.StuckRunDetection)
        {
            b.StuckRunDetection = d.StuckRunDetection;
            changes.Add(new FieldChange("stuck_run_detection", server.StuckRunDetection.ToString(), d.StuckRunDetection.ToString()));
        }

        // Only managed when the manifest set it explicitly (server computes it otherwise).
        if (d.MaxRunDurationSeconds is { } desiredMrd && server.MaxRunDurationSeconds != desiredMrd)
        {
            b.MaxRunDurationSeconds = desiredMrd;
            changes.Add(new FieldChange("max_run_duration", server.MaxRunDurationSeconds.ToString(CultureInfo.InvariantCulture), desiredMrd.ToString(CultureInfo.InvariantCulture)));
        }
    }

    // ── validation / normalization helpers ────────────────────────────────────────

    private static string NormalizeKind(string? kind, string label)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "http";
        }

        var k = kind.Trim().ToLowerInvariant();
        return k switch
        {
            "http" or "heartbeat" => k,
            _ => throw new ManifestException($"{label}: 'kind' must be 'http' or 'heartbeat' (got '{kind}')."),
        };
    }

    private static string NormalizeMethod(string? method, string label)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return JobDefaults.Method;
        }

        var m = method.Trim().ToUpperInvariant();
        if (!HttpMethods.Contains(m))
        {
            throw new ManifestException($"{label}: 'method' must be one of {string.Join(", ", HttpMethods)} (got '{method}').");
        }

        return m;
    }

    private static string NormalizeMisfire(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return JobDefaults.MisfirePolicy;
        }

        var v = value.Trim().ToLowerInvariant();
        if (!ManifestSchema.MisfirePolicies.Contains(v))
        {
            throw new ManifestException(
                $"{label}: 'misfire_policy' must be one of {string.Join(", ", ManifestSchema.MisfirePolicies)} (got '{value}').");
        }

        return v;
    }

    private static IReadOnlyList<int>? NormalizeStatusCodes(List<int>? codes, string label)
    {
        if (codes is null || codes.Count == 0)
        {
            return null;
        }

        foreach (var c in codes)
        {
            if (c is < 100 or > 599)
            {
                throw new ManifestException($"{label}: 'retry_on_status' values must be between 100 and 599 (got {c}).");
            }
        }

        return codes.Distinct().OrderBy(c => c).ToArray();
    }

    private static (ScheduleKind Kind, string? Cron, int? Interval) ResolveSchedule(ManifestJob job, string label)
    {
        var hasCron = !string.IsNullOrWhiteSpace(job.Schedule);
        var hasInterval = job.Interval.HasValue;

        if (hasCron && hasInterval)
        {
            throw new ManifestException($"{label}: specify either 'schedule' (cron) or 'interval', not both.");
        }

        if (!hasCron && !hasInterval)
        {
            throw new ManifestException($"{label}: a schedule is required — set 'schedule' (cron) or 'interval' (seconds).");
        }

        if (hasCron)
        {
            return (ScheduleKind.Cron, job.Schedule!.Trim(), null);
        }

        var interval = job.Interval!.Value;
        if (interval is < 10 or > 86_400)
        {
            throw new ManifestException($"{label}: 'interval' must be between 10 and 86400 seconds.");
        }

        return (ScheduleKind.Interval, null, interval);
    }

    private static void RejectHttpFields(ManifestJob job, string label)
    {
        var offenders = new List<string>();
        if (job.Method is not null) { offenders.Add("method"); }
        if (job.Url is not null) { offenders.Add("url"); }
        if (job.Headers is not null) { offenders.Add("headers"); }
        if (job.Body is not null) { offenders.Add("body"); }
        if (job.Timeout is not null) { offenders.Add("timeout"); }
        if (job.Retries is not null) { offenders.Add("retries"); }
        if (job.RetryBackoff is not null) { offenders.Add("retry_backoff"); }
        if (job.RetryOnTimeout is not null) { offenders.Add("retry_on_timeout"); }
        if (job.RetryOnStatus is not null) { offenders.Add("retry_on_status"); }
        if (job.SkipIfRunning is not null) { offenders.Add("skip_if_running"); }
        if (job.MisfirePolicy is not null) { offenders.Add("misfire_policy"); }

        if (offenders.Count > 0)
        {
            throw new ManifestException(
                $"{label}: the following field(s) are not valid for heartbeat jobs: {string.Join(", ", offenders)}.");
        }
    }

    private static MisfirePolicy ParseMisfireEnum(string value) =>
        value == "fire_once_now" ? MisfirePolicy.FireOnceNow : MisfirePolicy.DoNothing;

    // ── comparison helpers ────────────────────────────────────────────────────────

    private static string? Empty(string? s) => string.IsNullOrEmpty(s?.Trim()) ? null : s.Trim();

    private static bool StringEquals(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    private static string Display(string? s) => string.IsNullOrEmpty(s) ? "(none)" : s;

    private static (string Kind, string Value) DescribeServerSchedule(JobResponse server)
    {
        return string.Equals(server.ScheduleKind, "interval", StringComparison.OrdinalIgnoreCase)
            ? ("interval", $"every {server.IntervalSeconds}s")
            : ("cron", server.CronExpression ?? "(none)");
    }

    private static string DescribeDesiredSchedule(DesiredJob d) =>
        d.ScheduleKind == ScheduleKind.Interval ? $"every {d.IntervalSeconds}s" : d.CronExpression ?? "(none)";

    private static bool HeadersEqual(IReadOnlyDictionary<string, string>? server, IReadOnlyDictionary<string, string> desired)
    {
        var s = server ?? new Dictionary<string, string>();
        if (s.Count != desired.Count)
        {
            return false;
        }

        foreach (var (key, value) in desired)
        {
            if (!s.TryGetValue(key, out var serverValue) || !string.Equals(serverValue, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", headers.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    private static bool StatusCodesEqual(IReadOnlyList<int>? server, IReadOnlyList<int>? desired)
    {
        var s = (server ?? []).Distinct().OrderBy(c => c).ToArray();
        var d = (desired ?? []).Distinct().OrderBy(c => c).ToArray();
        return s.SequenceEqual(d);
    }

    private static string DescribeCodes(IReadOnlyList<int>? codes) =>
        codes is null || codes.Count == 0 ? "any non-2xx" : string.Join(",", codes.OrderBy(c => c));

    /// <summary>Mutable accumulator so only changed fields land on the immutable request.</summary>
    private sealed class UpdateBuilder
    {
        public string? Description { get; set; }
        public string? RunbookNotes { get; set; }
        public string? RunbookUrl { get; set; }
        public string? CronExpression { get; set; }
        public int? IntervalSeconds { get; set; }
        public string? Timezone { get; set; }
        public int? GraceSeconds { get; set; }
        public bool? StuckRunDetection { get; set; }
        public int? MaxRunDurationSeconds { get; set; }
        public string? HttpMethod { get; set; }
        public string? HttpUrl { get; set; }
        public Dictionary<string, string>? HttpHeaders { get; set; }
        public string? HttpBody { get; set; }
        public int? TimeoutSeconds { get; set; }
        public int? MaxRetries { get; set; }
        public int? RetryBackoffSeconds { get; set; }
        public bool? RetryOnTimeout { get; set; }
        public int[]? RetryOnStatusCodes { get; set; }
        public bool RetryOnStatusCodesSet { get; set; }
        public bool? SkipIfRunning { get; set; }
        public MisfirePolicy? MisfirePolicy { get; set; }

        public UpdateJobRequest Build() => new()
        {
            Description = Description,
            RunbookNotes = RunbookNotes,
            RunbookUrl = RunbookUrl,
            CronExpression = CronExpression,
            IntervalSeconds = IntervalSeconds,
            Timezone = Timezone,
            GraceSeconds = GraceSeconds,
            StuckRunDetection = StuckRunDetection,
            MaxRunDurationSeconds = MaxRunDurationSeconds,
            HttpMethod = HttpMethod,
            HttpUrl = HttpUrl,
            HttpHeaders = HttpHeaders,
            HttpBody = HttpBody,
            TimeoutSeconds = TimeoutSeconds,
            MaxRetries = MaxRetries,
            RetryBackoffSeconds = RetryBackoffSeconds,
            RetryOnTimeout = RetryOnTimeout,
            // Only emit codes when we deliberately set them (paired with retry_on_timeout).
            RetryOnStatusCodes = RetryOnStatusCodesSet ? RetryOnStatusCodes : null,
            SkipIfRunning = SkipIfRunning,
            MisfirePolicy = MisfirePolicy,
        };
    }
}
