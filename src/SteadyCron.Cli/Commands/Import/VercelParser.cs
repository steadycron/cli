using System.Text.Json;
using SteadyCron.Cli.Manifest;

namespace SteadyCron.Cli.Commands.Import;

/// <summary>
/// Parses a <c>vercel.json</c> file and converts its <c>crons</c> array into
/// <see cref="ManifestJob"/> objects. All I/O is separated out so this class is
/// directly unit-testable.
/// </summary>
internal static class VercelParser
{
    /// <summary>
    /// Parsed representation of a single Vercel cron entry.
    /// </summary>
    internal sealed record VercelCron(string Path, string Schedule);

    /// <summary>
    /// Result of parsing a <c>vercel.json</c>.
    /// </summary>
    internal sealed record ParseResult(
        IReadOnlyList<ManifestJob> Jobs,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Parses <paramref name="vercelJson"/> and builds <see cref="ManifestJob"/> HTTP jobs.
    /// </summary>
    /// <param name="vercelJson">Raw JSON text of the vercel.json file.</param>
    /// <param name="baseUrl">Required base URL prepended to each cron path.</param>
    /// <param name="cronSecretEnv">
    /// When set, adds an <c>Authorization: Bearer ${ENV}</c> header referencing this env-var name.
    /// </param>
    internal static ParseResult Parse(string vercelJson, string baseUrl, string? cronSecretEnv = null)
    {
        var warnings = new List<string>();

        List<VercelCron> crons;
        try
        {
            crons = ExtractCrons(vercelJson, warnings);
        }
        catch (JsonException ex)
        {
            return new ParseResult([], [$"Failed to parse vercel.json: {ex.Message}"]);
        }

        if (crons.Count == 0)
        {
            warnings.Add("No cron entries found in vercel.json.");
            return new ParseResult([], warnings);
        }

        Dictionary<string, string>? sharedHeaders = null;
        if (!string.IsNullOrWhiteSpace(cronSecretEnv))
        {
            sharedHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = $"Bearer ${{{cronSecretEnv}}}",
            };
        }

        var normalizedBase = baseUrl.TrimEnd('/');
        var jobs = new List<ManifestJob>(crons.Count);

        foreach (var cron in crons)
        {
            var path = cron.Path.StartsWith('/') ? cron.Path : "/" + cron.Path;
            var url = normalizedBase + path;
            var id = ManifestKeyGenerator.ToSlug(path.TrimStart('/'));
            id = DeduplicateId(id, jobs);

            jobs.Add(new ManifestJob
            {
                Id = id,
                Name = id,
                Kind = "http",
                Method = "GET",
                Url = url,
                Schedule = cron.Schedule,
                Timezone = "UTC",
                Headers = sharedHeaders is not null
                    ? new Dictionary<string, string>(sharedHeaders)
                    : null,
            });
        }

        return new ParseResult(jobs, warnings);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static List<VercelCron> ExtractCrons(string json, List<string> warnings)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("crons", out var cronsEl) ||
            cronsEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<VercelCron>();
        var idx = 0;

        foreach (var item in cronsEl.EnumerateArray())
        {
            idx++;

            if (!item.TryGetProperty("path", out var pathEl) ||
                pathEl.ValueKind != JsonValueKind.String)
            {
                warnings.Add($"crons[{idx - 1}]: missing or non-string 'path'; skipping.");
                continue;
            }

            if (!item.TryGetProperty("schedule", out var schedEl) ||
                schedEl.ValueKind != JsonValueKind.String)
            {
                warnings.Add($"crons[{idx - 1}]: missing or non-string 'schedule'; skipping.");
                continue;
            }

            var path = pathEl.GetString()!;
            var schedule = schedEl.GetString()!;

            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(schedule))
            {
                warnings.Add($"crons[{idx - 1}]: empty 'path' or 'schedule'; skipping.");
                continue;
            }

            result.Add(new VercelCron(path, schedule));
        }

        return result;
    }

    private static string DeduplicateId(string id, IReadOnlyList<ManifestJob> existing)
    {
        var taken = new HashSet<string>(existing.Select(j => j.Id ?? string.Empty), StringComparer.Ordinal);
        if (!taken.Contains(id))
        {
            return id;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{id}-{suffix}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return id + "-" + ManifestKeyGenerator.FromLine(id);
    }
}
