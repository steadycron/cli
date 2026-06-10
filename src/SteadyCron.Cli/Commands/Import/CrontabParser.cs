using System.Text;
using SteadyCron.Cli.Manifest;

namespace SteadyCron.Cli.Commands.Import;

/// <summary>
/// Parses crontab files (user and system format) into <see cref="ManifestJob"/> objects.
/// All I/O is separated out so this class is directly unit-testable.
/// </summary>
internal static class CrontabParser
{
    /// <summary>
    /// Result of parsing one or more crontab lines.
    /// </summary>
    internal sealed record ParseResult(
        IReadOnlyList<ManifestJob> Jobs,
        IReadOnlyList<string> Warnings,
        int HttpCount,
        int HeartbeatCount,
        int SkippedCount);

    /// <summary>
    /// Parses <paramref name="lines"/> into a list of <see cref="ManifestJob"/> entries.
    /// </summary>
    /// <param name="lines">Raw crontab lines.</param>
    /// <param name="isSystem">
    /// When true the line format is: 5 schedule fields + username + command.
    /// When false (user crontab): 5 schedule fields + command.
    /// </param>
    /// <param name="forceAs">
    /// <c>"auto"</c> (default), <c>"http"</c>, or <c>"heartbeat"</c>.
    /// </param>
    internal static ParseResult Parse(
        IEnumerable<string> lines,
        bool isSystem,
        string forceAs = "auto")
    {
        var jobs = new List<ManifestJob>();
        var warnings = new List<string>();
        var httpCount = 0;
        var heartbeatCount = 0;
        var skippedCount = 0;

        string? pendingComment = null;
        var ambiguousSystemDetected = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Blank line — clear pending comment
            if (string.IsNullOrWhiteSpace(line))
            {
                pendingComment = null;
                continue;
            }

            // Comment line — capture as candidate name for the next entry
            if (line.TrimStart().StartsWith('#'))
            {
                pendingComment = line.TrimStart()[1..].Trim();
                continue;
            }

            // Environment assignment (e.g. MAILTO="", PATH=..., FOO=bar)
            if (IsEnvAssignment(line))
            {
                pendingComment = null;
                continue;
            }

            // Macro or normal entry
            string cronExpr;
            string commandText;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('@'))
            {
                // Macro form: @hourly command...
                if (!TryExpandMacro(trimmed, out cronExpr!, out commandText!, out var macroWarning))
                {
                    warnings.Add(macroWarning!);
                    skippedCount++;
                    pendingComment = null;
                    continue;
                }
            }
            else
            {
                // Standard schedule fields
                if (!TrySplitScheduleAndCommand(line, isSystem, out cronExpr!, out commandText!, out var splitWarning))
                {
                    // Ambiguity check: try to guess system format
                    if (!isSystem && !ambiguousSystemDetected && LooksLikeSystemCrontab(line))
                    {
                        warnings.Add(
                            "Line may be a system crontab (6 fields before the command). " +
                            "If jobs are parsed incorrectly, re-run with --system.");
                        ambiguousSystemDetected = true;
                    }

                    warnings.Add($"Skipping unparseable line: {splitWarning}");
                    skippedCount++;
                    pendingComment = null;
                    continue;
                }
            }

            // Derive name: pending comment → else generate from command
            var name = !string.IsNullOrWhiteSpace(pendingComment)
                ? pendingComment
                : null;

            var id = name is not null
                ? ManifestKeyGenerator.ToSlug(name)
                : ManifestKeyGenerator.FromLine(rawLine.Trim());

            // Ensure id uniqueness within this parse (append suffix when clash occurs)
            id = DeduplicateId(id, jobs);

            // Classify command
            var mode = forceAs;
            HttpInfo? httpInfo = null;

            if (mode == "auto")
            {
                if (TryParseHttpCommand(commandText, out httpInfo))
                {
                    mode = "http";
                }
                else
                {
                    mode = "heartbeat";
                }
            }
            else if (mode == "http")
            {
                if (!TryParseHttpCommand(commandText, out httpInfo))
                {
                    // Forced http but can't find URL; skip with warning
                    warnings.Add(
                        $"Could not extract a URL from command (forced --as http), skipping: {commandText}");
                    skippedCount++;
                    pendingComment = null;
                    continue;
                }
            }
            // mode == "heartbeat" → no URL extraction needed

            ManifestJob job;
            if (mode == "http")
            {
                job = new ManifestJob
                {
                    Id = id,
                    Name = name ?? id,
                    Kind = "http",
                    Method = httpInfo!.Method,
                    Url = httpInfo.Url,
                    Headers = httpInfo.Headers is { Count: > 0 } ? httpInfo.Headers : null,
                    Body = httpInfo.Body,
                    Schedule = cronExpr,
                    Timezone = "UTC",
                };
                httpCount++;
            }
            else
            {
                job = new ManifestJob
                {
                    Id = id,
                    Name = name ?? id,
                    Kind = "heartbeat",
                    Schedule = cronExpr,
                    Timezone = "UTC",
                };
                heartbeatCount++;
            }

            jobs.Add(job);
            pendingComment = null;
        }

        return new ParseResult(jobs, warnings, httpCount, heartbeatCount, skippedCount);
    }

    // ── Schedule splitting ────────────────────────────────────────────────────────

    private static bool TrySplitScheduleAndCommand(
        string line,
        bool isSystem,
        out string cronExpr,
        out string commandText,
        out string? warning)
    {
        cronExpr = commandText = string.Empty;
        warning = null;

        // Walk through the line collecting field offsets, then capture the remainder as command
        int fieldsNeeded = isSystem ? 6 : 5;
        var pos = 0;
        var fieldTexts = new string[5];
        var fieldIdx = 0;

        for (var f = 0; f < fieldsNeeded; f++)
        {
            // skip leading whitespace
            while (pos < line.Length && char.IsWhiteSpace(line[pos]))
            {
                pos++;
            }

            if (pos >= line.Length)
            {
                warning = $"Insufficient fields ({f} of {fieldsNeeded}): {line.TrimEnd()}";
                return false;
            }

            var start = pos;
            while (pos < line.Length && !char.IsWhiteSpace(line[pos]))
            {
                pos++;
            }

            if (f < 5)
            {
                fieldTexts[fieldIdx++] = line[start..pos];
            }
            // The 6th field (system username) is intentionally consumed and discarded
        }

        // Skip whitespace before command
        while (pos < line.Length && char.IsWhiteSpace(line[pos]))
        {
            pos++;
        }

        commandText = pos < line.Length ? line[pos..] : string.Empty;

        if (string.IsNullOrWhiteSpace(commandText))
        {
            warning = $"Entry has no command: {line.TrimEnd()}";
            return false;
        }

        cronExpr = string.Join(" ", fieldTexts);
        return true;
    }

    private static bool TryExpandMacro(
        string trimmedLine,
        out string cronExpr,
        out string commandText,
        out string? warning)
    {
        cronExpr = commandText = string.Empty;
        warning = null;

        var spaceIdx = trimmedLine.IndexOf(' ');
        var macro = spaceIdx >= 0
            ? trimmedLine[..spaceIdx].ToLowerInvariant()
            : trimmedLine.ToLowerInvariant();

        commandText = spaceIdx >= 0 ? trimmedLine[(spaceIdx + 1)..].Trim() : string.Empty;

        cronExpr = macro switch
        {
            "@yearly" or "@annually" => "0 0 1 1 *",
            "@monthly" => "0 0 1 * *",
            "@weekly" => "0 0 * * 0",
            "@daily" or "@midnight" => "0 0 * * *",
            "@hourly" => "0 * * * *",
            "@reboot" => null!,
            _ => null!,
        };

        if (cronExpr is null)
        {
            warning = macro == "@reboot"
                ? "@reboot entries cannot be scheduled; skipping."
                : $"Unknown macro '{macro}'; skipping.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commandText))
        {
            warning = $"Macro entry '{macro}' has no command; skipping.";
            return false;
        }

        return true;
    }

    // ── Command classification ────────────────────────────────────────────────────

    private static bool TryParseHttpCommand(string command, out HttpInfo? info)
    {
        var tokens = TokenizeShell(command);
        if (tokens.Count == 0)
        {
            info = null;
            return false;
        }

        var prog = tokens[0];

        // Bare URL
        if (prog.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            prog.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            info = new HttpInfo(prog, "GET", null, null);
            return true;
        }

        var isCurl = prog.Equals("curl", StringComparison.OrdinalIgnoreCase) ||
                     prog.EndsWith("/curl", StringComparison.OrdinalIgnoreCase);
        var isWget = prog.Equals("wget", StringComparison.OrdinalIgnoreCase) ||
                     prog.EndsWith("/wget", StringComparison.OrdinalIgnoreCase);

        if (!isCurl && !isWget)
        {
            info = null;
            return false;
        }

        string? url = null;
        var method = "GET";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? body = null;

        for (var i = 1; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (isCurl)
            {
                if ((t is "-X" or "--request") && i + 1 < tokens.Count)
                {
                    method = tokens[++i].ToUpperInvariant();
                }
                else if ((t is "-H" or "--header") && i + 1 < tokens.Count)
                {
                    var headerLine = tokens[++i];
                    var colon = headerLine.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[headerLine[..colon].Trim()] = headerLine[(colon + 1)..].Trim();
                    }
                }
                else if ((t is "-d" or "--data" or "--data-raw" or "--data-binary") && i + 1 < tokens.Count)
                {
                    body = tokens[++i];
                    if (method == "GET")
                    {
                        method = "POST";
                    }
                }
                else if (t == "--url" && i + 1 < tokens.Count)
                {
                    url = tokens[++i];
                }
                else if (!t.StartsWith('-') && url is null &&
                         (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    url = t;
                }
                // skip other flags (e.g. -f, -s, -S, -L, --fail, --silent)
            }
            else // wget
            {
                if ((t is "-O" or "--output-document") && i + 1 < tokens.Count)
                {
                    i++; // skip output path
                }
                else if (!t.StartsWith('-') &&
                         (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    url = t;
                }
                // skip -q / --quiet, --spider, etc.
            }
        }

        if (url is null)
        {
            info = null;
            return false;
        }

        info = new HttpInfo(url, method, headers.Count > 0 ? headers : null, body);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static bool IsEnvAssignment(string line)
    {
        var trimmed = line.TrimStart();
        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        var key = trimmed[..eq];
        // Key must be alphanumeric/underscore with no whitespace
        return key.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static bool LooksLikeSystemCrontab(string line)
    {
        // Heuristic: if splitting by whitespace gives >=7 tokens and the 6th looks like a username
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 7)
        {
            return false;
        }

        var candidate = parts[5];
        return candidate.All(c => char.IsLetterOrDigit(c) || c is '_' or '-') &&
               !candidate.Contains('/') &&
               candidate.Length <= 32;
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

    // ── Shell tokenizer ───────────────────────────────────────────────────────────

    internal static List<string> TokenizeShell(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (inSingle)
            {
                if (c == '\'')
                {
                    inSingle = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (inDouble)
            {
                if (c == '"')
                {
                    inDouble = false;
                }
                else if (c == '\\' && i + 1 < command.Length)
                {
                    i++;
                    current.Append(command[i]);
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '\'')
            {
                inSingle = true;
            }
            else if (c == '"')
            {
                inDouble = true;
            }
            else if (c == '\\' && i + 1 < command.Length)
            {
                i++;
                current.Append(command[i]);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    // ── Data records ──────────────────────────────────────────────────────────────

    internal sealed record HttpInfo(
        string Url,
        string Method,
        Dictionary<string, string>? Headers,
        string? Body);
}
