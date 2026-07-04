using SteadyCron.Cli.Commands.Manifest;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// SPEC-20c §8.6: the generated cheat sheet (<c>steadycron_example.yaml</c>, produced by both
/// `manifest scaffold` and `init`) must always pass `steadycron validate`. This is the acceptance
/// gate for the §8 schema corrections — it fails the build the moment the cheat sheet drifts from
/// the real manifest schema again.
/// </summary>
public sealed class CheatSheetValidationTests
{
    [Fact]
    public void YamlBoilerplate_passesValidate()
    {
        var loader = new ManifestLoader();
        var manifest = loader.Parse(InitCommand.YamlBoilerplate, "steadycron_example.yaml", name => $"dummy-{name}");

        var result = new ManifestValidator().Validate(manifest);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }
}
