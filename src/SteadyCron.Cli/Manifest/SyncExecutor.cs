using SteadyCron.Cli.Api;

namespace SteadyCron.Cli.Manifest;

/// <summary>A single failed action during apply (kept so the run can continue and report at the end).</summary>
public sealed record SyncFailure(string JobName, string Action, string Message);

/// <summary>Tallies of what an apply did.</summary>
public sealed record SyncApplyResult(
    int Created,
    int Updated,
    int Paused,
    int Resumed,
    int Pruned,
    IReadOnlyList<SyncFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>Kinds of apply events surfaced to the caller for live progress output.</summary>
public enum SyncEventKind
{
    Created,
    Updated,
    Paused,
    Resumed,
    Pruned,
    Failed,
}

public sealed record SyncEvent(SyncEventKind Kind, string JobName, string? Detail = null);

/// <summary>Applies a <see cref="SyncPlan"/> against the API. Continues past per-job failures.</summary>
public sealed class SyncExecutor
{
    private readonly SteadyCronClient _client;

    public SyncExecutor(SteadyCronClient client)
    {
        _client = client;
    }

    public async Task<SyncApplyResult> ApplyAsync(
        SyncPlan plan,
        bool prune,
        Action<SyncEvent>? onEvent = null,
        CancellationToken ct = default)
    {
        int created = 0, updated = 0, paused = 0, resumed = 0, pruned = 0;
        var failures = new List<SyncFailure>();

        foreach (var create in plan.Creates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _client.CreateJobAsync(create.Request, ct);
                created++;
                onEvent?.Invoke(new SyncEvent(SyncEventKind.Created, create.Desired.Name));
            }
            catch (SteadyCronApiException ex)
            {
                failures.Add(new SyncFailure(create.Desired.Name, "create", ex.Message));
                onEvent?.Invoke(new SyncEvent(SyncEventKind.Failed, create.Desired.Name, ex.Message));
            }
        }

        foreach (var update in plan.ChangedUpdates)
        {
            ct.ThrowIfCancellationRequested();
            var name = update.Desired.Name;
            var id = update.Server.Id;

            if (update.HasFieldChanges)
            {
                try
                {
                    await _client.UpdateJobAsync(id, update.Request, ct);
                    updated++;
                    onEvent?.Invoke(new SyncEvent(SyncEventKind.Updated, name));
                }
                catch (SteadyCronApiException ex)
                {
                    failures.Add(new SyncFailure(name, "update", ex.Message));
                    onEvent?.Invoke(new SyncEvent(SyncEventKind.Failed, name, ex.Message));
                    continue; // don't attempt pause/resume if the update failed
                }
            }

            switch (update.Pause)
            {
                case PauseTransition.Pause:
                    try
                    {
                        await _client.PauseJobAsync(id, ct);
                        paused++;
                        onEvent?.Invoke(new SyncEvent(SyncEventKind.Paused, name));
                    }
                    catch (SteadyCronApiException ex)
                    {
                        failures.Add(new SyncFailure(name, "pause", ex.Message));
                        onEvent?.Invoke(new SyncEvent(SyncEventKind.Failed, name, ex.Message));
                    }

                    break;

                case PauseTransition.Resume:
                    try
                    {
                        await _client.ResumeJobAsync(id, ct);
                        resumed++;
                        onEvent?.Invoke(new SyncEvent(SyncEventKind.Resumed, name));
                    }
                    catch (SteadyCronApiException ex)
                    {
                        failures.Add(new SyncFailure(name, "resume", ex.Message));
                        onEvent?.Invoke(new SyncEvent(SyncEventKind.Failed, name, ex.Message));
                    }

                    break;

                case PauseTransition.None:
                default:
                    break;
            }
        }

        if (prune)
        {
            foreach (var orphan in plan.Orphans)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await _client.DeleteJobAsync(orphan.Server.Id, ct);
                    pruned++;
                    onEvent?.Invoke(new SyncEvent(SyncEventKind.Pruned, orphan.Server.Name));
                }
                catch (SteadyCronApiException ex)
                {
                    failures.Add(new SyncFailure(orphan.Server.Name, "prune", ex.Message));
                    onEvent?.Invoke(new SyncEvent(SyncEventKind.Failed, orphan.Server.Name, ex.Message));
                }
            }
        }

        return new SyncApplyResult(created, updated, paused, resumed, pruned, failures);
    }
}
