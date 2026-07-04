using SteadyCron.Cli.Configuration;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ConfigFileTests
{
    [Fact]
    public void Abbreviate_pathUnderHome_replacesHomeWithTilde()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows always shows the expanded path — see SPEC-20c §2.1.
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, ".config", "steadycron", "config.json");

        Assert.Equal("~/.config/steadycron/config.json".Replace('/', Path.DirectorySeparatorChar), ConfigFile.Abbreviate(path));
    }

    [Fact]
    public void Abbreviate_pathOutsideHome_isUnchanged()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string path = "/etc/steadycron/config.json";

        Assert.Equal(path, ConfigFile.Abbreviate(path));
    }
}
