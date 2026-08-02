using SteadyCron.Cli.Commands.Manifest;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// SPEC-21 §4.4 / acceptance: "`scaffold` and `add` render from the same per-resource... no
/// duplicated template strings — asserted by a unit test or a shared-fixture comparison." The
/// cheat sheet (`InitCommand.YamlBoilerplate`) keeps its own full prose (it's a documentation
/// artifact, not decomposed into the generator's block templates — see PR notes), but every
/// enumerated value it advertises must still come from — and therefore can never drift from —
/// the same <see cref="ManifestSchema"/> the `add` commands validate against. This is exactly the
/// class of bug SPEC-20c fixed (the shaping block, the invalid HEAD method): these tests make it
/// impossible for the cheat sheet's documented value list and the real accepted value list to
/// silently diverge again.
/// </summary>
public sealed class ManifestSchemaDriftGuardTests
{
    [Fact]
    public void CheatSheet_httpMethodList_matchesManifestSchema()
    {
        const string marker = "# GET (default) | POST | PUT | PATCH | DELETE";
        Assert.Contains(marker, InitCommand.YamlBoilerplate, StringComparison.Ordinal);

        var listed = marker.TrimStart('#', ' ').Replace("GET (default)", "GET")
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(ManifestSchema.HttpMethods.OrderBy(m => m), listed.OrderBy(m => m));
    }

    [Fact]
    public void CheatSheet_misfirePolicies_bothAppearAlongsideTheField()
    {
        foreach (var policy in ManifestSchema.MisfirePolicies)
        {
            Assert.Contains(policy, InitCommand.YamlBoilerplate, StringComparison.Ordinal);
        }

        // No extra policy beyond the two documented ones.
        Assert.Equal(2, ManifestSchema.MisfirePolicies.Count);
    }

    [Fact]
    public void CheatSheet_tagColorList_matchesManifestSchema()
    {
        const string start = "# Valid colors: ";
        var startIdx = InitCommand.YamlBoilerplate.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIdx >= 0, "Cheat sheet should document the valid tag colors.");

        var endIdx = InitCommand.YamlBoilerplate.IndexOf(".\n", startIdx, StringComparison.Ordinal);
        var raw = InitCommand.YamlBoilerplate[(startIdx + start.Length)..endIdx];

        var listed = raw
            .Replace("#", string.Empty, StringComparison.Ordinal)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            ManifestSchema.TagColors.OrderBy(c => c, StringComparer.Ordinal),
            listed.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void CheatSheet_channelKinds_allAppearInTheChannelsSection()
    {
        foreach (var kind in ManifestSchema.ChannelKinds)
        {
            Assert.Contains($"kind: {kind}", InitCommand.YamlBoilerplate, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CheatSheet_jobKindList_matchesManifestSchema()
    {
        const string marker = "# http (default) | heartbeat | agent";
        Assert.Contains(marker, InitCommand.YamlBoilerplate, StringComparison.Ordinal);

        var listed = marker.TrimStart('#', ' ').Replace("http (default)", "http")
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(ManifestSchema.JobKinds.OrderBy(k => k, StringComparer.Ordinal),
            listed.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void CheatSheet_documentsEveryJobKindAsAWorkedExample()
    {
        // Every kind gets a real block in the scaffold, not just a mention in the `kind:` comment.
        foreach (var kind in ManifestSchema.JobKinds)
        {
            Assert.Contains($"kind: {kind}", InitCommand.YamlBoilerplate, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CheatSheet_agentCostPeriods_matchManifestSchema()
    {
        const string marker = "# day | month";
        Assert.Contains(marker, InitCommand.YamlBoilerplate, StringComparison.Ordinal);

        var listed = marker.TrimStart('#', ' ')
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(ManifestSchema.AgentCostPeriods.OrderBy(p => p, StringComparer.Ordinal),
            listed.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void JobKindSchema_matchesTheWireContract()
    {
        // ManifestSchema must not fork its own list — the manifest and the API client have to
        // agree on what a kind is called, or `kind: agent` validates locally and 400s on apply.
        Assert.Equal(SteadyCron.Cli.Api.Models.JobKinds.All, ManifestSchema.JobKinds);
        Assert.Equal(3, ManifestSchema.JobKinds.Count);
    }

    [Fact]
    public void HttpMethodSchema_neverIncludesHead()
    {
        // The exact regression SPEC-20c fixed in the cheat sheet — HEAD isn't a valid job method
        // per the DB check constraint. Guarding the schema itself means every consumer (the
        // validator, `add job --method`, and the cheat sheet's documented list) is protected at once.
        Assert.DoesNotContain(ManifestSchema.HttpMethods, m => string.Equals(m, "HEAD", StringComparison.OrdinalIgnoreCase));
    }
}
