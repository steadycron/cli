using System.Text.RegularExpressions;
using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Schema and cross-reference linter for a parsed <see cref="ManifestFile"/>.
/// All checks run locally — no network calls.
/// </summary>
public sealed class ManifestValidator
{
    // Each cron field allows: number, *, ?, L, range (a-b), step (*/n or a-b/n), lists (a,b…).
    // This is a structural check only; the server provides authoritative validation on apply.
    private static readonly Regex CronFieldPattern =
        new(@"^(\*|[?L]|(\d+(-\d+)?(,\d+(-\d+)?)*)(\/\d+)?|\*\/\d+)$", RegexOptions.Compiled);

    public sealed record ValidationResult(IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Validates the manifest and returns a result with all found errors.
    /// Does not throw — accumulates all errors for actionable output.
    /// </summary>
    public ValidationResult Validate(ManifestFile manifest)
    {
        var errors = new List<string>();

        ValidateJobs(manifest, errors);
        ValidateChannels(manifest, errors);
        ValidateTags(manifest, errors);
        ValidateVariables(manifest, errors);

        return new ValidationResult(errors);
    }

    // ── Jobs ──────────────────────────────────────────────────────────────────────

    private static void ValidateJobs(ManifestFile manifest, List<string> errors)
    {
        if (manifest.Jobs is null || manifest.Jobs.Count == 0)
        {
            return;
        }

        var declaredTagKeys = BuildTagKeySet(manifest);
        var declaredChannelRefs = BuildChannelRefSet(manifest);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < manifest.Jobs.Count; i++)
        {
            var job = manifest.Jobs[i];
            var label = string.IsNullOrWhiteSpace(job.Name) ? $"jobs[{i}]" : $"job '{job.Name}'";

            if (string.IsNullOrWhiteSpace(job.Name))
            {
                errors.Add($"{label}: 'name' is required.");
                continue;
            }

            if (!seenNames.Add(job.Name))
            {
                errors.Add($"{label}: duplicate job name '{job.Name}'. Names must be unique.");
            }

            var kind = job.Kind?.Trim().ToLowerInvariant() ?? JobKinds.Http;
            if (!ManifestSchema.JobKinds.Contains(kind))
            {
                errors.Add($"{label}: 'kind' must be one of {string.Join(", ", ManifestSchema.JobKinds)} (got '{job.Kind}').");
                continue;
            }

            var hasCron = !string.IsNullOrWhiteSpace(job.Schedule);
            var hasInterval = job.Interval.HasValue;

            if (hasCron && hasInterval)
            {
                errors.Add($"{label}: specify either 'schedule' (cron) or 'interval', not both.");
            }
            else if (!hasCron && !hasInterval)
            {
                errors.Add($"{label}: a schedule is required — set 'schedule' (cron) or 'interval' (seconds).");
            }
            else if (hasCron)
            {
                ValidateCronExpression(job.Schedule!, label, errors);
            }
            else if (hasInterval && job.Interval!.Value is < 10 or > 86_400)
            {
                errors.Add($"{label}: 'interval' must be between 10 and 86400 seconds (got {job.Interval}).");
            }

            if (JobKinds.IsHttp(kind))
            {
                ValidateHttpJob(job, label, errors);
            }
            else
            {
                ValidatePingDrivenJob(job, label, errors);
            }

            if (JobKinds.IsAgent(kind))
            {
                ValidateAgentJob(job, label, errors);
            }
            else
            {
                var agentFields = JobMapper.AgentFieldNames(job);
                if (agentFields.Count > 0)
                {
                    errors.Add($"{label}: field(s) only valid for agent monitors: {string.Join(", ", agentFields)}.");
                }
            }

            // v2 tag references — only checked when tags are declared in this manifest
            if (job.Tags is { Count: > 0 } && declaredTagKeys.Count > 0)
            {
                foreach (var tagRef in job.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tagRef) && !declaredTagKeys.Contains(tagRef))
                    {
                        errors.Add($"{label}: tag '{tagRef}' is not declared in the manifest's 'tags' section.");
                    }
                }
            }

            // v2 rule channel references — only checked when channels are declared
            if (job.Rules is { Count: > 0 } && declaredChannelRefs.Count > 0)
            {
                foreach (var rule in job.Rules)
                {
                    if (rule.Channel is not null && !declaredChannelRefs.Contains(rule.Channel))
                    {
                        errors.Add(
                            $"{label}: rule references channel '{rule.Channel}', " +
                            $"which is not declared in the manifest's 'channels' section.");
                    }
                }
            }
        }
    }

    private static void ValidateHttpJob(ManifestJob job, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(job.Url))
        {
            errors.Add($"{label}: 'url' is required for HTTP jobs.");
        }

        if (job.Timeout.HasValue && job.Timeout.Value <= 0)
        {
            errors.Add($"{label}: 'timeout' must be a positive number of seconds.");
        }

        if (job.Retries.HasValue && job.Retries.Value < 0)
        {
            errors.Add($"{label}: 'retries' cannot be negative.");
        }

        if (job.RetryBackoff.HasValue && job.RetryBackoff.Value < 0)
        {
            errors.Add($"{label}: 'retry_backoff' cannot be negative.");
        }

        if (job.RetryOnStatus is { Count: > 0 })
        {
            foreach (var code in job.RetryOnStatus)
            {
                if (code is < 100 or > 599)
                {
                    errors.Add($"{label}: 'retry_on_status' value {code} is out of range (100–599).");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(job.Method))
        {
            var m = job.Method.Trim().ToUpperInvariant();
            if (!ManifestSchema.HttpMethods.Contains(m))
            {
                errors.Add($"{label}: 'method' must be one of {string.Join(", ", ManifestSchema.HttpMethods)} (got '{job.Method}').");
            }
        }

        if (!string.IsNullOrWhiteSpace(job.MisfirePolicy))
        {
            var mp = job.MisfirePolicy.Trim().ToLowerInvariant();
            if (!ManifestSchema.MisfirePolicies.Contains(mp))
            {
                errors.Add($"{label}: 'misfire_policy' must be one of {string.Join(", ", ManifestSchema.MisfirePolicies)} (got '{job.MisfirePolicy}').");
            }
        }

        var offenders = new List<string>();
        if (job.Grace is not null) { offenders.Add("grace"); }
        if (job.StuckRunDetection is not null) { offenders.Add("stuck_run_detection"); }
        if (job.MaxRunDurationSeconds is not null) { offenders.Add("max_run_duration_seconds"); }
        if (offenders.Count > 0)
        {
            errors.Add($"{label}: field(s) not valid for HTTP jobs: {string.Join(", ", offenders)}.");
        }
    }

    private static void ValidatePingDrivenJob(ManifestJob job, string label, List<string> errors)
    {
        if (job.Grace.HasValue && job.Grace.Value is < 0 or > 86_400)
        {
            errors.Add($"{label}: 'grace' must be between 0 and 86400 seconds.");
        }

        if (job.MaxRunDurationSeconds.HasValue && job.MaxRunDurationSeconds.Value is < 60 or > 86_400)
        {
            errors.Add($"{label}: 'max_run_duration_seconds' must be between 60 and 86400 seconds.");
        }

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
            errors.Add($"{label}: field(s) not valid for ping-driven jobs: {string.Join(", ", offenders)}.");
        }
    }

    /// <summary>
    /// Agent-only checks. Bounds mirror the API's <c>ck_jobs_agent_rule_bounds</c> so a manifest
    /// fails here, naming the field, rather than as a 400 halfway through an apply.
    /// </summary>
    private static void ValidateAgentJob(ManifestJob job, string label, List<string> errors)
    {
        if (job.StuckRunDetection is false)
        {
            errors.Add(
                $"{label}: 'stuck_run_detection' cannot be disabled on an agent monitor — " +
                "enforcing the /start ping is what the kind is for.");
        }

        if (job.ItemsLabel is { Length: > JobDefaults.ItemsLabelMaxLength })
        {
            errors.Add($"{label}: 'items_label' must be {JobDefaults.ItemsLabelMaxLength} characters or fewer.");
        }

        if (job.RuleMaxCostUsdPerRun < 0)
        {
            errors.Add($"{label}: 'rule_max_cost_usd_per_run' cannot be negative (0 or omitted means no ceiling).");
        }

        if (job.RuleMaxCostUsdPerPeriod < 0)
        {
            errors.Add($"{label}: 'rule_max_cost_usd_per_period' cannot be negative (0 or omitted means no ceiling).");
        }

        foreach (var (field, value) in new[]
                 {
                     ("rule_max_steps", job.RuleMaxSteps),
                     ("rule_max_tool_calls", job.RuleMaxToolCalls),
                     ("rule_max_duration_ms", job.RuleMaxDurationMs),
                 })
        {
            if (value < 0)
            {
                errors.Add($"{label}: '{field}' cannot be negative (0 or omitted means no ceiling).");
            }
        }

        if (!string.IsNullOrWhiteSpace(job.RuleCostPeriod))
        {
            var period = job.RuleCostPeriod.Trim().ToLowerInvariant();
            if (!ManifestSchema.AgentCostPeriods.Contains(period))
            {
                errors.Add(
                    $"{label}: 'rule_cost_period' must be one of {string.Join(", ", ManifestSchema.AgentCostPeriods)} " +
                    $"(got '{job.RuleCostPeriod}').");
            }
            else if (job.RuleMaxCostUsdPerPeriod is null or 0)
            {
                errors.Add($"{label}: 'rule_cost_period' requires 'rule_max_cost_usd_per_period' to be set.");
            }
        }
    }

    // ── Cron expression validation ────────────────────────────────────────────────

    private static void ValidateCronExpression(string expression, string label, List<string> errors)
    {
        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            errors.Add($"{label}: 'schedule' must be a 5-field cron expression " +
                       $"(minute hour dom month dow), got {parts.Length} field(s): '{expression}'.");
            return;
        }

        var fieldNames = new[] { "minute", "hour", "day-of-month", "month", "day-of-week" };
        for (var i = 0; i < parts.Length; i++)
        {
            if (!CronFieldPattern.IsMatch(parts[i]))
            {
                errors.Add($"{label}: cron field '{fieldNames[i]}' has invalid value '{parts[i]}'.");
            }
        }
    }

    // ── Channels ─────────────────────────────────────────────────────────────────

    private static void ValidateChannels(ManifestFile manifest, List<string> errors)
    {
        if (manifest.Channels is null)
        {
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < manifest.Channels.Count; i++)
        {
            var ch = manifest.Channels[i];
            var label = string.IsNullOrWhiteSpace(ch.Name) ? $"channels[{i}]" : $"channel '{ch.Name}'";

            if (string.IsNullOrWhiteSpace(ch.Name))
            {
                errors.Add($"{label}: 'name' is required.");
            }

            if (string.IsNullOrWhiteSpace(ch.Kind))
            {
                errors.Add($"{label}: 'kind' is required.");
            }

            if (ch.Id is not null && !seenIds.Add(ch.Id))
            {
                errors.Add($"{label}: duplicate channel id '{ch.Id}'.");
            }
        }
    }

    // ── Tags ─────────────────────────────────────────────────────────────────────

    private static void ValidateTags(ManifestFile manifest, List<string> errors)
    {
        if (manifest.Tags is null)
        {
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < manifest.Tags.Count; i++)
        {
            var tag = manifest.Tags[i];
            var label = $"tags[{i}]";

            if (string.IsNullOrWhiteSpace(tag.Key))
            {
                errors.Add($"{label}: 'key' is required.");
            }

            if (string.IsNullOrWhiteSpace(tag.Value))
            {
                errors.Add($"{label}: 'value' is required.");
            }

            if (tag.Id is not null && !seenIds.Add(tag.Id))
            {
                errors.Add($"{label}: duplicate tag id '{tag.Id}'.");
            }
        }
    }

    // ── Variables ─────────────────────────────────────────────────────────────────

    private static void ValidateVariables(ManifestFile manifest, List<string> errors)
    {
        if (manifest.Variables is null)
        {
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < manifest.Variables.Count; i++)
        {
            var v = manifest.Variables[i];
            var label = string.IsNullOrWhiteSpace(v.Name) ? $"variables[{i}]" : $"variable '{v.Name}'";

            if (string.IsNullOrWhiteSpace(v.Name))
            {
                errors.Add($"{label}: 'name' is required.");
            }
            else if (!seenNames.Add(v.Name))
            {
                errors.Add($"{label}: duplicate variable name '{v.Name}'.");
            }

            if (v.Id is not null && !seenIds.Add(v.Id))
            {
                errors.Add($"{label}: duplicate variable id '{v.Id}'.");
            }
        }
    }

    // ── Reference set builders ────────────────────────────────────────────────────

    private static HashSet<string> BuildTagKeySet(ManifestFile manifest)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Tags is null)
        {
            return set;
        }

        foreach (var tag in manifest.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag.Key) && !string.IsNullOrWhiteSpace(tag.Value))
            {
                set.Add($"{tag.Key}:{tag.Value}");
            }

            if (tag.Id is not null)
            {
                set.Add(tag.Id);
            }
        }

        return set;
    }

    private static HashSet<string> BuildChannelRefSet(ManifestFile manifest)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Channels is null)
        {
            return set;
        }

        foreach (var ch in manifest.Channels)
        {
            if (ch.Id is not null) { set.Add(ch.Id); }
            if (!string.IsNullOrWhiteSpace(ch.Name)) { set.Add(ch.Name!); }
        }

        return set;
    }
}
