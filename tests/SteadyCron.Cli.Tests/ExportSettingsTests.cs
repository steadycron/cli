using Spectre.Console.Cli;
using SteadyCron.Cli.Commands.Export;
using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ExportSettingsTests
{
    [Fact]
    public void EffectiveWriteEnvPath_null_whenFlagNotPassed()
    {
        var settings = new ExportSettings();
        Assert.Null(settings.EffectiveWriteEnvPath);
    }

    [Fact]
    public void EffectiveWriteEnvPath_defaultsToSteadycronSecretsEnv_whenFlagPassedWithNoValue()
    {
        var settings = new ExportSettings { WriteEnv = new FlagValue<string> { IsSet = true, Value = string.Empty } };
        Assert.Equal(ManifestEnvironment.DefaultSecretsFile, settings.EffectiveWriteEnvPath);
    }

    [Fact]
    public void EffectiveWriteEnvPath_usesGivenPath_whenFlagPassedWithValue()
    {
        var settings = new ExportSettings { WriteEnv = new FlagValue<string> { IsSet = true, Value = "custom.env" } };
        Assert.Equal("custom.env", settings.EffectiveWriteEnvPath);
    }
}
