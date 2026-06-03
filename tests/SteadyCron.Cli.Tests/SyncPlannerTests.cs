using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class SyncPlannerTests
{
    private readonly SyncPlanner _planner = new(new JobMapper());

    [Fact]
    public void New_manifest_job_is_planned_as_create()
    {
        var plan = _planner.Plan(
            [Http("new-job")],
            []);

        Assert.Single(plan.Creates);
        Assert.Equal("new-job", plan.Creates[0].Desired.Name);
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.Orphans);
    }

    [Fact]
    public void Unchanged_job_is_a_noop()
    {
        var manifest = Http("job", url: "https://example.com");
        var server = ServerHttp("job", url: "https://example.com");

        var plan = _planner.Plan([manifest], [server]);

        Assert.Empty(plan.Creates);
        Assert.Single(plan.NoOps);
        Assert.Empty(plan.ChangedUpdates);
    }

    [Fact]
    public void Changed_job_is_an_update()
    {
        var manifest = Http("job", url: "https://new.example.com");
        var server = ServerHttp("job", url: "https://old.example.com");

        var plan = _planner.Plan([manifest], [server]);

        Assert.Single(plan.ChangedUpdates);
        Assert.Contains(plan.ChangedUpdates[0].Changes, c => c.Field == "url");
    }

    [Fact]
    public void Server_job_not_in_manifest_is_an_orphan()
    {
        var plan = _planner.Plan(
            [Http("kept")],
            [ServerHttp("kept"), ServerHttp("gone")]);

        Assert.Single(plan.Orphans);
        Assert.Equal("gone", plan.Orphans[0].Server.Name);
    }

    [Fact]
    public void Kind_mismatch_is_a_conflict()
    {
        var manifest = Http("job");
        var server = ServerHttp("job") with { Kind = "heartbeat" };

        var plan = _planner.Plan([manifest], [server]);

        Assert.Single(plan.Conflicts);
        Assert.Empty(plan.ChangedUpdates);
        Assert.Empty(plan.Creates);
    }

    [Fact]
    public void Ambiguous_server_names_are_a_conflict()
    {
        var plan = _planner.Plan(
            [Http("dup")],
            [ServerHttp("dup"), ServerHttp("dup")]);

        Assert.Single(plan.Conflicts);
    }

    [Fact]
    public void Duplicate_manifest_names_throw()
    {
        Assert.Throws<ManifestException>(() =>
            _planner.Normalize([Http("same"), Http("same")]));
    }

    [Fact]
    public void Pause_transition_when_manifest_pauses_an_active_job()
    {
        var manifest = Http("job");
        manifest.Paused = true;
        var server = ServerHttp("job") with { Status = "success" }; // not paused

        var plan = _planner.Plan([manifest], [server]);

        var update = Assert.Single(plan.ChangedUpdates);
        Assert.Equal(PauseTransition.Pause, update.Pause);
    }

    [Fact]
    public void Resume_transition_when_manifest_unpauses_a_paused_job()
    {
        var manifest = Http("job"); // paused defaults to false
        var server = ServerHttp("job") with { Status = "paused" };

        var plan = _planner.Plan([manifest], [server]);

        var update = Assert.Single(plan.ChangedUpdates);
        Assert.Equal(PauseTransition.Resume, update.Pause);
    }

    // ── builders ──────────────────────────────────────────────────────────────────

    private static ManifestJob Http(string name, string url = "https://example.com") => new()
    {
        Name = name,
        Kind = "http",
        Method = "GET",
        Url = url,
        Interval = 300,
    };

    private static JobResponse ServerHttp(string name, string url = "https://example.com") => new()
    {
        Id = Guid.NewGuid(),
        Kind = "http",
        Name = name,
        ScheduleKind = "interval",
        IntervalSeconds = 300,
        Timezone = "UTC",
        HttpMethod = "GET",
        HttpUrl = url,
        TimeoutSeconds = 60,
        MaxRetries = 0,
        RetryBackoffSeconds = 30,
        RetryOnTimeout = true,
        SkipIfRunning = false,
        MisfirePolicy = "do_nothing",
    };
}
