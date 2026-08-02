using System.Text.Json.Serialization;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// The YAML manifest shape. v2 adds <c>namespace</c>, <c>channels</c>, <c>tags</c>,
/// <c>variables</c>, <c>shaping</c>, and <c>id</c>/<c>rules</c>/<c>tags</c> on jobs.
/// v1 (jobs-only, name-keyed, no namespace) is still accepted with a deprecation warning.
/// </summary>
public sealed class ManifestFile
{
    /// <summary>1 (legacy) or 2 (current). Omitting version defaults to 2.</summary>
    public int? Version { get; set; }

    /// <summary>Account namespace. Required by <c>--prune</c> and cross-file merges.</summary>
    public string? Namespace { get; set; }

    public List<ManifestJob>? Jobs { get; set; }

    // ── v2-only top-level resources ──────────────────────────────────────────────

    public List<ManifestChannel>? Channels { get; set; }
    public List<ManifestTag>? Tags { get; set; }
    public List<ManifestVariable>? Variables { get; set; }
    public ManifestShaping? Shaping { get; set; }
}

/// <summary>A single job declared in the manifest.</summary>
public sealed class ManifestJob
{
    /// <summary>Stable reconciliation key (v2). When absent, <c>name</c> is the key (v1 behavior).</summary>
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary><c>http</c> (default), <c>heartbeat</c>, or <c>agent</c>.</summary>
    public string? Kind { get; set; }

    public string? Description { get; set; }

    /// <summary>Markdown runbook notes (max 4000 chars).</summary>
    public string? RunbookNotes { get; set; }

    /// <summary>Link to an external runbook (max 2048 chars).</summary>
    public string? RunbookUrl { get; set; }

    // ── Schedule (exactly one of these) ──────────────────────────────────────────
    /// <summary>A 5-field cron expression, e.g. <c>"0 9 * * 1"</c>.</summary>
    public string? Schedule { get; set; }

    /// <summary>A fixed interval in seconds (alternative to <see cref="Schedule"/>).</summary>
    public int? Interval { get; set; }

    public string? Timezone { get; set; }

    /// <summary>Create/keep the job in a paused state.</summary>
    public bool? Paused { get; set; }

    // ── HTTP jobs ────────────────────────────────────────────────────────────────
    public string? Method { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
    public int? Timeout { get; set; }
    public int? Retries { get; set; }
    public int? RetryBackoff { get; set; }
    public bool? RetryOnTimeout { get; set; }
    public List<int>? RetryOnStatus { get; set; }
    public bool? SkipIfRunning { get; set; }

    /// <summary><c>do_nothing</c> (default) or <c>fire_once_now</c>.</summary>
    public string? MisfirePolicy { get; set; }

    // ── Heartbeat and agent jobs (both ping-driven) ──────────────────────────────
    public int? Grace { get; set; }

    /// <summary>Heartbeat only — agent monitors force it on, and declaring it false is rejected.</summary>
    public bool? StuckRunDetection { get; set; }

    /// <summary>
    /// Maximum in-flight run duration in seconds. On an agent monitor this is the run clock —
    /// distinct from <see cref="Grace"/>, which governs the schedule window (docs/AGENTS.md §2.2).
    /// <para>The wire name matches the API's field exactly; <c>max_run_duration</c> is still
    /// accepted on input as a legacy alias and normalised away by <see cref="ManifestLoader"/>.</para>
    /// </summary>
    public int? MaxRunDurationSeconds { get; set; }

    /// <summary>
    /// Legacy spelling of <see cref="MaxRunDurationSeconds"/>. Accepted so existing manifests keep
    /// working; never sent to the API, which only knows the <c>_seconds</c> name.
    /// </summary>
    [JsonIgnore]
    public int? MaxRunDuration { get; set; }

    // ── Agent monitors ───────────────────────────────────────────────────────────
    // Field names match the API's job DTO exactly (docs/AGENTS.md §8), so a manifest reads the
    // same as the API reference. Cost ceilings are USD; 0 clears one.

    /// <summary>Record a success ping carrying no parseable run report as unverified. Default true.</summary>
    public bool? ReportRequired { get; set; }

    /// <summary>What this agent produces — "tickets", "rows", "documents". Max 40 characters.</summary>
    public string? ItemsLabel { get; set; }

    /// <summary>Treat a run reporting zero items produced as a failure. Default true.</summary>
    public bool? RuleEmptyResultEnabled { get; set; }

    /// <summary>Alert when a single run costs more than this, in USD.</summary>
    public decimal? RuleMaxCostUsdPerRun { get; set; }

    /// <summary>Alert when spend over <see cref="RuleCostPeriod"/> exceeds this, in USD.</summary>
    public decimal? RuleMaxCostUsdPerPeriod { get; set; }

    /// <summary><c>day</c> or <c>month</c> (default). Buckets use the job's own timezone.</summary>
    public string? RuleCostPeriod { get; set; }

    /// <summary>Alert when a run's step count exceeds this — loop detection.</summary>
    public int? RuleMaxSteps { get; set; }

    /// <summary>Alert when a run's tool-call count exceeds this.</summary>
    public int? RuleMaxToolCalls { get; set; }

    /// <summary>Alert when a run's measured duration exceeds this, in milliseconds.</summary>
    public int? RuleMaxDurationMs { get; set; }

    // ── v2-only per-job fields ───────────────────────────────────────────────────
    /// <summary>Tag references as <c>"key:value"</c> strings. Must resolve to declared tags.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Alert rules attached to this job.</summary>
    public List<ManifestRule>? Rules { get; set; }
}

/// <summary>An alert channel declared at the manifest level (v2).</summary>
public sealed class ManifestChannel
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public Dictionary<string, string>? Config { get; set; }
}

/// <summary>A tag declared at the manifest level (v2).</summary>
public sealed class ManifestTag
{
    public string? Id { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
    public string? Color { get; set; }
}

/// <summary>A template variable declared at the manifest level (v2).</summary>
public sealed class ManifestVariable
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    /// <summary>Can use <c>${ENV_VAR}</c> interpolation.</summary>
    public string? Value { get; set; }
}

/// <summary>An alert rule attached to a job (v2).</summary>
public sealed class ManifestRule
{
    /// <summary>Channel <c>id</c> or <c>name</c> reference.</summary>
    public string? Channel { get; set; }
    public string? Trigger { get; set; }
    public string? Severity { get; set; }
    public Dictionary<string, object>? Params { get; set; }
    public int? DedupWindowSeconds { get; set; }
}

/// <summary>Account-level shaping / rate-limit hints (v2).</summary>
public sealed class ManifestShaping
{
    public int? MaxConcurrentJobs { get; set; }
    public int? MaxJobsPerMinute { get; set; }
}
