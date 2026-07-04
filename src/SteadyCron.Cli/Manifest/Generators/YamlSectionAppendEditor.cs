using System.Text.RegularExpressions;

namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>YAML implementation of <see cref="IManifestFileEditor"/> (SPEC-21 §3.2).</summary>
public sealed class YamlSectionAppendEditor : IManifestFileEditor
{
    private const int CanonicalIndentWidth = 2;

    private static readonly IReadOnlyDictionary<ManifestSection, string> SectionKeys = new Dictionary<ManifestSection, string>
    {
        [ManifestSection.Jobs] = "jobs",
        [ManifestSection.Channels] = "channels",
        [ManifestSection.Tags] = "tags",
        [ManifestSection.Variables] = "variables",
    };

    public string EmptyFileHeader() => "version: 2\n# namespace: my-project\n";

    public bool ResourceExists(string content, ManifestSection section, params (string Field, string Value)[] matchFields)
    {
        var key = SectionKeys[section];
        var lines = SplitLines(content).Lines;
        GuardSafeToEdit(content, lines, key);

        var (headerIndex, insertAt) = FindSectionRange(lines, key);
        if (headerIndex < 0)
        {
            return false;
        }

        foreach (var item in EnumerateListItems(lines, headerIndex + 1, insertAt))
        {
            if (matchFields.All(mf => string.Equals(ExtractFieldValue(lines, item, mf.Field), mf.Value, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    public string Insert(string content, ManifestSection section, string block)
    {
        var key = SectionKeys[section];
        var (lines, newline, trailingNewline) = SplitLines(content);
        GuardSafeToEdit(content, lines, key);

        var (headerIndex, insertAt) = FindSectionRange(lines, key);
        var width = DetectIndentWidth(lines);
        var blockLines = ReindentBlock(block, width);

        var result = new List<string>(lines);
        if (headerIndex < 0)
        {
            if (result.Count > 0 && result[^1].Trim().Length != 0)
            {
                result.Add(string.Empty);
            }

            result.Add($"{key}:");
            result.AddRange(blockLines);
        }
        else
        {
            result.InsertRange(insertAt, blockLines);
        }

        var joined = string.Join(newline, result);
        return trailingNewline ? joined + newline : joined;
    }

    // ── Safety guards (SPEC-21 §5.4) ──────────────────────────────────────────────

    private static void GuardSafeToEdit(string content, IReadOnlyList<string> lines, string sectionKey)
    {
        if (content.Contains('\t'))
        {
            throw new ManifestEditException(
                "cannot safely edit this manifest — it contains tabs. Use --dry-run and paste the block in manually.");
        }

        var loosePattern = new Regex($@"^{Regex.Escape(sectionKey)}:");
        var strictPattern = new Regex($@"^{Regex.Escape(sectionKey)}:\s*(#.*)?$");
        foreach (var line in lines)
        {
            if (loosePattern.IsMatch(line) && !strictPattern.IsMatch(line))
            {
                throw new ManifestEditException(
                    $"cannot safely edit this manifest — '{sectionKey}:' is written in flow style. " +
                    "Use --dry-run and paste the block in manually.");
            }
        }
    }

    // ── Section location ───────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the section header and the index to insert at (right after the section's last
    /// content line). Returns <c>(-1, -1)</c> if the section doesn't exist.
    /// </summary>
    /// <remarks>
    /// A section's list items may be indented under the key (<c>jobs:\n  - id: x</c>) or written
    /// at the *same* column as the key itself — <c>jobs:\n- id: x</c> is equally valid YAML, and
    /// is exactly what this CLI's own server-side <c>export</c> emits. A column-0 line is only a
    /// new top-level key (ending the section) when it *isn't* a sequence item; a column-0 line
    /// starting with <c>-</c> is still this section's next entry.
    /// </remarks>
    private static (int HeaderIndex, int InsertAt) FindSectionRange(IReadOnlyList<string> lines, string key)
    {
        var headerPattern = new Regex($@"^{Regex.Escape(key)}:\s*(#.*)?$");
        var headerIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (headerPattern.IsMatch(lines[i]))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            return (-1, -1);
        }

        var lastContent = headerIndex;
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                continue; // blank line inside the section — keep scanning
            }

            if (char.IsWhiteSpace(line[0]) || line[0] == '-')
            {
                lastContent = i; // indented content, or a zero-indent sequence item — still this section
                continue;
            }

            break; // column-0, non-blank, not a list item — a new top-level key ends the section
        }

        return (headerIndex, lastContent + 1);
    }

    private static IEnumerable<(int Start, int End)> EnumerateListItems(IReadOnlyList<string> lines, int from, int to)
    {
        var itemPattern = new Regex(@"^\s*-\s");
        var starts = new List<int>();
        for (var i = from; i < to; i++)
        {
            if (itemPattern.IsMatch(lines[i]))
            {
                starts.Add(i);
            }
        }

        for (var i = 0; i < starts.Count; i++)
        {
            yield return (starts[i], i + 1 < starts.Count ? starts[i + 1] : to);
        }
    }

    private static string? ExtractFieldValue(IReadOnlyList<string> lines, (int Start, int End) item, string field)
    {
        // A list item's first field shares its line with the "- " marker (e.g. "  - id: x"); every
        // other field is on its own line indented to align with it (e.g. "    name: y").
        var pattern = new Regex($@"^\s*(?:-\s*)?{Regex.Escape(field)}:\s*(.*)$");
        for (var i = item.Start; i < item.End; i++)
        {
            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                return NormalizeYamlScalar(match.Groups[1].Value.Trim());
            }
        }

        return null;
    }

    private static string NormalizeYamlScalar(string raw)
    {
        if (raw.Length == 0)
        {
            return raw;
        }

        if (raw[0] is '"' or '\'')
        {
            var closeIdx = raw.IndexOf(raw[0], 1);
            if (closeIdx > 0)
            {
                return raw[1..closeIdx];
            }

            return raw;
        }

        var hashIdx = raw.IndexOf(" #", StringComparison.Ordinal);
        return (hashIdx >= 0 ? raw[..hashIdx] : raw).TrimEnd();
    }

    // ── Indentation ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects the file's list-item indent — the number of spaces before a sequence item's
    /// <c>-</c>. Zero is a valid, common result (<c>jobs:\n- id: x</c>, e.g. this CLI's own
    /// server-side <c>export</c> output) — not just the "indented under the key" style
    /// (<c>jobs:\n  - id: x</c>) this CLI's own hand-written templates use.
    /// </summary>
    private static int DetectIndentWidth(IReadOnlyList<string> lines)
    {
        var pattern = new Regex(@"^( *)-\s");
        foreach (var line in lines)
        {
            var match = pattern.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value.Length;
            }
        }

        return CanonicalIndentWidth;
    }

    /// <summary>
    /// Re-indents a block authored at the canonical 2-space item indent to match
    /// <paramref name="targetItemIndent"/>. Uses a constant additive offset (not a per-level
    /// multiplicative scale) because a sequence item's fields are always exactly 2 columns past
    /// its own <c>-</c> regardless of where that <c>-</c> sits — including at column 0, where a
    /// multiplicative scale would incorrectly flatten every field to column 0 too.
    /// </summary>
    private static List<string> ReindentBlock(string block, int targetItemIndent)
    {
        var blockLines = block.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var delta = targetItemIndent - CanonicalIndentWidth;
        if (delta == 0)
        {
            return [.. blockLines];
        }

        var result = new List<string>(blockLines.Length);
        foreach (var line in blockLines)
        {
            if (line.Length == 0)
            {
                result.Add(line);
                continue;
            }

            var trimmed = line.TrimStart(' ');
            var leading = line.Length - trimmed.Length;
            var newLeading = Math.Max(0, leading + delta);
            result.Add(new string(' ', newLeading) + trimmed);
        }

        return result;
    }

    // ── Line splitting (preserves CRLF and trailing-newline presence) ────────────

    private static (List<string> Lines, string Newline, bool TrailingNewline) SplitLines(string content)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trailingNewline = content.Length > 0 && content[^1] == '\n';
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n').ToList();
        if (trailingNewline && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return (lines, newline, trailingNewline);
    }
}
