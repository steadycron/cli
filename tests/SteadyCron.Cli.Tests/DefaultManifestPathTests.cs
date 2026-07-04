using SteadyCron.Cli.Commands.Sync;
using SteadyCron.Cli.Commands.Validate;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// Regression: `sync`/`plan`/`apply`/`validate` used to default to `jobs.yaml` when no path was
/// given, but `steadycron init` (and `manifest scaffold`/`add`) all write `steadycron.yaml` — so
/// `steadycron apply --dry-run` right after `init` failed with "Manifest file not found:
/// jobs.yaml". The default must match what the rest of the CLI actually generates.
/// </summary>
public sealed class DefaultManifestPathTests
{
    [Fact]
    public void SyncSettings_defaultsToSteadycronYaml_whenNoPathsGiven()
    {
        var settings = new SyncSettings();
        Assert.Equal(["steadycron.yaml"], settings.EffectivePaths);
    }

    [Fact]
    public void SyncSettings_usesGivenPaths_whenProvided()
    {
        var settings = new SyncSettings { Paths = ["manifests/"] };
        Assert.Equal(["manifests/"], settings.EffectivePaths);
    }

    [Fact]
    public void ApplySettings_inheritsTheSameDefault()
    {
        var settings = new ApplySettings();
        Assert.Equal(["steadycron.yaml"], settings.EffectivePaths);
    }

    [Fact]
    public void PlanSettings_inheritsTheSameDefault()
    {
        var settings = new PlanSettings();
        Assert.Equal(["steadycron.yaml"], settings.EffectivePaths);
    }

    [Fact]
    public void ValidateSettings_defaultsToSteadycronYaml_whenNoPathsGiven()
    {
        var settings = new ValidateSettings();
        Assert.Equal(["steadycron.yaml"], settings.EffectivePaths);
    }
}
