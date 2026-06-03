using SteadyCron.Cli.Configuration;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ConfigResolverTests
{
    private static ConfigResolver Build(
        Dictionary<string, string?>? env = null, ConfigFile? file = null)
    {
        env ??= new Dictionary<string, string?>();
        return new ConfigResolver(
            key => env.GetValueOrDefault(key),
            () => "/tmp/steadycron/config.json",
            _ => file);
    }

    [Fact]
    public void Defaults_when_nothing_is_set()
    {
        var config = Build().Resolve(flagApiKey: null, flagApiUrl: null);

        Assert.Equal(CliConfiguration.DefaultApiUrl, config.ApiUrl);
        Assert.Equal(ConfigSource.Default, config.ApiUrlSource);
        Assert.False(config.HasApiKey);
    }

    [Fact]
    public void Flag_beats_env_beats_file()
    {
        var env = new Dictionary<string, string?>
        {
            [ConfigResolver.ApiKeyEnvVar] = "sc_from_env",
            [ConfigResolver.ApiUrlEnvVar] = "https://env.example.com",
        };
        var file = new ConfigFile { ApiKey = "sc_from_file", ApiUrl = "https://file.example.com" };

        var resolver = Build(env, file);

        var withFlag = resolver.Resolve("sc_from_flag", "https://flag.example.com");
        Assert.Equal("sc_from_flag", withFlag.ApiKey);
        Assert.Equal(ConfigSource.Flag, withFlag.ApiKeySource);
        Assert.Equal("https://flag.example.com", withFlag.ApiUrl);

        var noFlag = resolver.Resolve(null, null);
        Assert.Equal("sc_from_env", noFlag.ApiKey);
        Assert.Equal(ConfigSource.Environment, noFlag.ApiKeySource);
    }

    [Fact]
    public void File_used_when_no_flag_or_env()
    {
        var file = new ConfigFile { ApiKey = "sc_from_file", ApiUrl = "https://file.example.com" };
        var config = Build(env: null, file: file).Resolve(null, null);

        Assert.Equal("sc_from_file", config.ApiKey);
        Assert.Equal(ConfigSource.ConfigFile, config.ApiKeySource);
        Assert.Equal("https://file.example.com", config.ApiUrl);
        Assert.Equal(ConfigSource.ConfigFile, config.ApiUrlSource);
    }

    [Fact]
    public void Url_trailing_slash_is_normalized()
    {
        var config = Build().Resolve(null, "https://example.com/");
        Assert.Equal("https://example.com", config.ApiUrl);
    }

    [Fact]
    public void Api_key_is_masked_to_prefix()
    {
        var config = Build().Resolve("sc_abcdefgh1234567890", null);
        Assert.Equal("sc_abcdefgh…", config.MaskedApiKey);
    }
}
