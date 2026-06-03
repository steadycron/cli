using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Computes a <see cref="SyncPlan"/> by reconciling a list of desired jobs (from the manifest)
/// against the current server jobs. Jobs are matched by <c>name</c>.
/// </summary>
public sealed class SyncPlanner
{
    private readonly JobMapper _mapper;

    public SyncPlanner(JobMapper mapper)
    {
        _mapper = mapper;
    }

    /// <summary>
    /// Validates and normalizes manifest jobs into desired state. Throws <see cref="ManifestException"/>
    /// on invalid fields or duplicate names. Runs without any server data so manifests fail fast.
    /// </summary>
    public IReadOnlyList<DesiredJob> Normalize(IReadOnlyList<ManifestJob> manifestJobs)
    {
        var desired = new List<DesiredJob>(manifestJobs.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < manifestJobs.Count; i++)
        {
            var d = _mapper.ToDesired(manifestJobs[i], i);
            if (!seen.Add(d.Name))
            {
                throw new ManifestException(
                    $"Duplicate job name '{d.Name}' in manifest. Names must be unique — they are the reconciliation key.");
            }

            desired.Add(d);
        }

        return desired;
    }

    /// <summary>Convenience overload that normalizes the manifest and then plans against the server.</summary>
    public SyncPlan Plan(IReadOnlyList<ManifestJob> manifestJobs, IReadOnlyList<JobResponse> serverJobs) =>
        Plan(Normalize(manifestJobs), serverJobs);

    public SyncPlan Plan(IReadOnlyList<DesiredJob> desired, IReadOnlyList<JobResponse> serverJobs)
    {
        var conflicts = new List<PlannedConflict>();

        // Index server jobs by name; names that collide are ambiguous and excluded from matching.
        var serverByName = serverJobs
            .GroupBy(j => j.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var creates = new List<PlannedCreate>();
        var updates = new List<PlannedUpdate>();
        var noOps = new List<PlannedUpdate>();
        var matchedServerIds = new HashSet<Guid>();

        foreach (var d in desired)
        {
            if (!serverByName.TryGetValue(d.Name, out var matches) || matches.Count == 0)
            {
                creates.Add(new PlannedCreate(d, _mapper.ToCreateRequest(d)));
                continue;
            }

            if (matches.Count > 1)
            {
                conflicts.Add(new PlannedConflict(
                    d.Name,
                    $"{matches.Count} server jobs share this name; cannot reconcile unambiguously. Rename or delete the duplicates."));
                continue;
            }

            var server = matches[0];
            matchedServerIds.Add(server.Id);

            if (!string.Equals(server.Kind, d.Kind, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new PlannedConflict(
                    d.Name,
                    $"manifest declares kind '{d.Kind}' but the server job is '{server.Kind}'. A job's kind cannot be changed; delete it and re-create."));
                continue;
            }

            var result = _mapper.BuildUpdate(d, server);
            var pause = ResolvePause(d, server);
            var update = new PlannedUpdate(d, server, result.Request, result.Changes, pause);

            if (update.IsNoOp)
            {
                noOps.Add(update);
            }
            else
            {
                updates.Add(update);
            }
        }

        var orphans = serverJobs
            .Where(j => !matchedServerIds.Contains(j.Id))
            .Select(j => new PlannedOrphan(j))
            .ToList();

        return new SyncPlan(creates, updates, noOps, orphans, conflicts);
    }

    private static PauseTransition ResolvePause(DesiredJob desired, JobResponse server)
    {
        // The server derives "paused" status from is_paused; pausing/resuming is done via dedicated
        // endpoints (not PATCH), so we plan it as a separate transition.
        var serverPaused = string.Equals(server.Status, "paused", StringComparison.OrdinalIgnoreCase);
        if (desired.Paused == serverPaused)
        {
            return PauseTransition.None;
        }

        return desired.Paused ? PauseTransition.Pause : PauseTransition.Resume;
    }
}
