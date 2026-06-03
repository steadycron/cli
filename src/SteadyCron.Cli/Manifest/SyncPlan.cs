using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Manifest;

/// <summary>Whether a planned change also flips the job's paused state.</summary>
public enum PauseTransition
{
    None,
    Pause,
    Resume,
}

/// <summary>A job present in the manifest but not on the server — it will be created.</summary>
public sealed record PlannedCreate(DesiredJob Desired, CreateJobRequest Request);

/// <summary>
/// A manifest job matched to an existing server job. May carry field changes, a pause transition,
/// or both. When neither is present it is a no-op.
/// </summary>
public sealed record PlannedUpdate(
    DesiredJob Desired,
    JobResponse Server,
    UpdateJobRequest Request,
    IReadOnlyList<FieldChange> Changes,
    PauseTransition Pause)
{
    public bool HasFieldChanges => Changes.Count > 0;

    public bool IsNoOp => Changes.Count == 0 && Pause == PauseTransition.None;
}

/// <summary>A job on the server with no manifest counterpart. Reported, and deleted only with <c>--prune</c>.</summary>
public sealed record PlannedOrphan(JobResponse Server);

/// <summary>A manifest job that cannot be reconciled (e.g. a kind change, or an ambiguous name match).</summary>
public sealed record PlannedConflict(string Name, string Reason);

/// <summary>The full reconciliation plan produced by <see cref="SyncPlanner"/>.</summary>
public sealed record SyncPlan(
    IReadOnlyList<PlannedCreate> Creates,
    IReadOnlyList<PlannedUpdate> Updates,
    IReadOnlyList<PlannedUpdate> NoOps,
    IReadOnlyList<PlannedOrphan> Orphans,
    IReadOnlyList<PlannedConflict> Conflicts)
{
    /// <summary>Updates that carry real changes (field changes and/or a pause transition).</summary>
    public IReadOnlyList<PlannedUpdate> ChangedUpdates =>
        Updates.Where(u => !u.IsNoOp).ToList();

    public bool HasActionableChanges =>
        Creates.Count > 0 || ChangedUpdates.Count > 0 || Orphans.Count > 0;
}
