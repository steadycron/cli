namespace SteadyCron.Cli.Manifest;

/// <summary>
/// The friendly YAML manifest shape documented on the marketing site
/// (<c>name</c>/<c>kind</c>/<c>schedule</c>/<c>url</c>/…). Properties are nullable so the mapper can
/// distinguish "omitted" from "explicitly set". YAML keys are snake_case (e.g. <c>retry_on_status</c>).
/// </summary>
public sealed class ManifestFile
{
    /// <summary>Optional manifest schema version. Currently informational.</summary>
    public int? Version { get; set; }

    public List<ManifestJob>? Jobs { get; set; }
}

/// <summary>A single job declared in the manifest.</summary>
public sealed class ManifestJob
{
    public string? Name { get; set; }

    /// <summary><c>http</c> (default) or <c>heartbeat</c>.</summary>
    public string? Kind { get; set; }

    public string? Description { get; set; }

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

    // ── Heartbeat jobs ───────────────────────────────────────────────────────────
    public int? Grace { get; set; }
    public bool? StuckRunDetection { get; set; }
    public int? MaxRunDuration { get; set; }
}
