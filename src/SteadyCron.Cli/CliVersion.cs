using System.Reflection;

namespace SteadyCron.Cli;

/// <summary>The informational version of the running CLI, read from assembly metadata.</summary>
public static class CliVersion
{
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(info))
        {
            return "0.0.0";
        }

        // Strip the "+<commit-sha>" source-revision suffix the SDK appends.
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
