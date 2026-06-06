using System.Text;
using System.Text.RegularExpressions;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Parses <c>.env</c> files into a flat name→value map for manifest <c>${...}</c> interpolation.
/// Supported per line:
/// <list type="bullet">
///   <item><c>KEY=value</c>, optional <c>export </c> prefix, blank lines and <c>#</c> comments.</item>
///   <item>single-quoted values are literal; double-quoted values unescape <c>\n \t \r \" \\</c>.</item>
///   <item>unquoted values are trimmed and have a trailing <c> # comment</c> stripped.</item>
/// </list>
/// Later files (and later lines) override earlier ones.
/// </summary>
public static class EnvFile
{
    private static readonly Regex KeyPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Reads and merges the given env files. Throws <see cref="ManifestException"/> on a
    /// missing file or a malformed line.</summary>
    public static IReadOnlyDictionary<string, string> Load(IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new ManifestException($"Env file not found: {path}");
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException ex)
            {
                throw new ManifestException($"Could not read env file '{path}': {ex.Message}", ex);
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var (key, value) = ParseLine(lines[i], path, i + 1);
                if (key is not null)
                {
                    result[key] = value!;
                }
            }
        }

        return result;
    }

    private static (string? Key, string? Value) ParseLine(string raw, string path, int lineNumber)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line[0] == '#')
        {
            return (null, null);
        }

        if (line.StartsWith("export ", StringComparison.Ordinal))
        {
            line = line[7..].TrimStart();
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            throw new ManifestException(
                $"Invalid line {lineNumber} in env file '{path}': expected KEY=VALUE.");
        }

        var key = line[..eq].Trim();
        if (!KeyPattern.IsMatch(key))
        {
            throw new ManifestException(
                $"Invalid variable name '{key}' on line {lineNumber} of env file '{path}': " +
                $"must start with a letter or underscore and contain only [A-Za-z0-9_].");
        }

        return (key, ParseValue(line[(eq + 1)..]));
    }

    private static string ParseValue(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return Unescape(value[1..^1]);
        }

        // Unquoted: strip a trailing inline comment that is preceded by whitespace.
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == '#' && char.IsWhiteSpace(value[i - 1]))
            {
                return value[..i].TrimEnd();
            }
        }

        return value;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => next,
                });
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }
}
