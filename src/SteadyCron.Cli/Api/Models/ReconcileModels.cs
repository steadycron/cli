namespace SteadyCron.Cli.Api.Models;

// ── POST /api/reconcile ──────────────────────────────────────────────────────────
// Contract aligned with Spec 13a §5. The manifest is sent as a structured object
// so the server can parse it directly.

public sealed record ReconcileRequest
{
    public required object Manifest { get; init; }
    public string? Namespace { get; init; }
    public bool DryRun { get; init; }
    public bool Prune { get; init; }
}

public sealed record ReconcileResponse
{
    public bool Applied { get; init; }
    public IReadOnlyList<ReconcilePlanItem> Creates { get; init; } = [];
    public IReadOnlyList<ReconcilePlanItemWithDiff> Updates { get; init; } = [];
    public IReadOnlyList<ReconcilePlanItem> Deletes { get; init; } = [];
    public IReadOnlyList<ReconcilePlanItem> NoChanges { get; init; } = [];
    public IReadOnlyList<ReconcileError> Errors { get; init; } = [];
}

/// <summary>A resource in the reconcile plan (create / delete / no-change).</summary>
public sealed record ReconcilePlanItem(
    string ResourceType,
    string? Id,
    string Name,
    string? Kind);

/// <summary>A resource that will be updated, with per-field diffs.</summary>
public sealed record ReconcilePlanItemWithDiff(
    string ResourceType,
    string? Id,
    string Name,
    string? Kind,
    IReadOnlyList<ReconcileFieldDiff> Diffs);

/// <summary>A single field change within an update.</summary>
public sealed record ReconcileFieldDiff(string Field, string? From, string? To);

/// <summary>A plan-level or per-resource error (limit violation, namespace conflict, etc.).</summary>
public sealed record ReconcileError(
    string? ResourceType,
    string? ResourceId,
    string? ResourceName,
    string Code,
    string Message);
