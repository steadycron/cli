namespace SteadyCron.Cli.Api.Models;

// ── POST /api/reconcile ──────────────────────────────────────────────────────────
// Contract aligned with Spec 13a §5 / docs/CRON_AS_CODE.md. The manifest is sent as a
// structured object so the server can parse it directly; the response is a structured
// plan with a per-action summary and a flat, action-keyed list of changes.

public sealed record ReconcileRequest
{
    public required object Manifest { get; init; }
    public string? Namespace { get; init; }
    public bool DryRun { get; init; }
    public bool Prune { get; init; }
}

/// <summary>
/// The server's structured plan (and the applied result when not a dry run). The wire shape is
/// <c>{ namespace, summary, changes, errors, applied }</c>: counts live in <see cref="Summary"/>
/// and every create/update/delete/no-change is one entry in <see cref="Changes"/>, distinguished
/// by <see cref="ReconcileChange.Action"/>.
/// </summary>
public sealed record ReconcileResponse
{
    public string? Namespace { get; init; }
    public ReconcileSummary Summary { get; init; } = new(0, 0, 0, 0, 0);
    public IReadOnlyList<ReconcileChange> Changes { get; init; } = [];
    public IReadOnlyList<ReconcileError> Errors { get; init; } = [];
    public bool Applied { get; init; }

    /// <summary>Total resources the plan would mutate (create + update + delete).</summary>
    public bool HasWork => Summary.Create > 0 || Summary.Update > 0 || Summary.Delete > 0;
}

/// <summary>Per-action counts for the plan.</summary>
public sealed record ReconcileSummary(int Create, int Update, int Delete, int NoChange, int Errors);

/// <summary>
/// One planned (or applied) change to a resource. <see cref="Action"/> is one of
/// <c>create</c> | <c>update</c> | <c>delete</c> | <c>no_change</c>. <see cref="Diff"/> is
/// populated for updates.
/// </summary>
public sealed record ReconcileChange(
    string Resource,
    string Key,
    string Action,
    IReadOnlyList<ReconcileFieldDiff>? Diff);

/// <summary>A single field change within an update.</summary>
public sealed record ReconcileFieldDiff(string Field, string? From, string? To);

/// <summary>A plan-level or per-resource error (limit violation, namespace conflict, etc.).</summary>
public sealed record ReconcileError(
    string? Resource,
    string? Key,
    string Code,
    string Message);
