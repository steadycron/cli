using System.Text.RegularExpressions;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Resolves <c>${NAME}</c> and <c>${NAME:-default}</c> placeholders in YAML manifest text
/// before deserialization. This is distinct from server-side <c>{{template_var}}</c>
/// substitution, which the CLI passes through untouched.
///
/// Rules:
/// - <c>${NAME}</c>          — replaced with the environment variable value; error if unset.
/// - <c>${NAME:-default}</c> — replaced with the env var value, or <c>default</c> if unset.
/// - <c>{{...}}</c>          — passed through unchanged (server-side template variables).
/// </summary>
public static class EnvInterpolator
{
    // Matches ${NAME} and ${NAME:-default}. NAME must start with a letter or underscore.
    // The default value stops at the first '}' (shell-style semantics) — it must not contain
    // a literal '}', so a placeholder can never swallow unrelated text (including a later
    // ${...} or {{...}}) up to the next closing brace found anywhere downstream.
    private static readonly Regex PlaceholderRegex = new(
        @"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?}",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces all <c>${...}</c> placeholders in <paramref name="text"/> with environment
    /// variable values. <paramref name="source"/> is used only in error messages.
    /// </summary>
    /// <exception cref="ManifestException">
    /// Thrown (exit 2) when a variable has no value and no default is specified.
    /// </exception>
    public static string Apply(string text, string source = "<manifest>",
        Func<string, string?>? getVar = null)
    {
        getVar ??= Environment.GetEnvironmentVariable;

        var errors = new List<string>();
        var result = PlaceholderRegex.Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            var hasDefault = match.Groups[2].Success;
            var defaultValue = match.Groups[2].Value;

            var value = getVar(name);
            if (value is not null)
            {
                return value;
            }

            if (hasDefault)
            {
                return defaultValue;
            }

            errors.Add(name);
            return match.Value; // placeholder; will be reported below
        });

        if (errors.Count == 1)
        {
            throw new ManifestException(
                $"'{source}' references undefined environment variable '${{{errors[0]}}}'. " +
                $"Set it in the environment or add a default with ${{{errors[0]}:-value}}.");
        }

        if (errors.Count > 1)
        {
            var list = string.Join(", ", errors.Select(e => $"${{{e}}}"));
            throw new ManifestException(
                $"'{source}' references {errors.Count} undefined environment variables: {list}. " +
                $"Set them or add defaults with ${{NAME:-value}}.");
        }

        return result;
    }

    /// <summary>
    /// Scans <paramref name="text"/> for <c>${NAME}</c> patterns and returns the distinct
    /// variable names referenced (whether or not they have defaults). Used to summarize
    /// required-env context on stderr during <c>export</c>.
    /// </summary>
    public static IReadOnlyList<string> FindPlaceholders(string text)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PlaceholderRegex.Matches(text))
        {
            names.Add(match.Groups[1].Value);
        }

        return [.. names.OrderBy(n => n, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Scans <paramref name="text"/> for placeholders that <b>must</b> be supplied — i.e. those
    /// referenced at least once without a <c>:-default</c>. Names that only ever appear with a
    /// default are excluded. Used by the env-file guardrail on <c>sync</c>/<c>plan</c>/<c>apply</c>.
    /// </summary>
    public static IReadOnlyList<string> FindRequiredPlaceholders(string text)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PlaceholderRegex.Matches(text))
        {
            if (!match.Groups[2].Success)
            {
                required.Add(match.Groups[1].Value);
            }
        }

        return [.. required.OrderBy(n => n, StringComparer.Ordinal)];
    }
}
