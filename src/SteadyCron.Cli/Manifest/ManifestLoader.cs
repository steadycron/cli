using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SteadyCron.Cli.Manifest;

/// <summary>Loads and parses YAML manifests into a merged <see cref="ManifestFile"/>.</summary>
public sealed class ManifestLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // ── Single-file API (used by tests and backward-compat code) ─────────────────

    /// <summary>Reads, interpolates env vars, and parses the manifest at <paramref name="path"/>.</summary>
    public ManifestFile LoadFromFile(string path, Func<string, string?>? getVar = null)
    {
        if (!File.Exists(path))
        {
            throw new ManifestException($"Manifest file not found: {path}");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ManifestException($"Could not read manifest '{path}': {ex.Message}", ex);
        }

        return Parse(text, path, getVar);
    }

    /// <summary>Parses manifest YAML text. <paramref name="source"/> is used only in error messages.</summary>
    public ManifestFile Parse(string text, string source = "<manifest>",
        Func<string, string?>? getVar = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ManifestException($"Manifest '{source}' is empty.");
        }

        // Substitute ${ENV_VAR} placeholders before YAML parse.
        text = EnvInterpolator.Apply(text, source, getVar);

        ManifestFile? manifest;
        try
        {
            manifest = Deserializer.Deserialize<ManifestFile>(text);
        }
        catch (YamlException ex)
        {
            var where = ex.Start.Line > 0 ? $" (line {ex.Start.Line})" : string.Empty;
            throw new ManifestException($"Invalid YAML in '{source}'{where}: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new ManifestException($"Manifest '{source}' did not contain any content.");
        }

        manifest.Jobs ??= [];
        return manifest;
    }

    // ── Multi-file / directory API ────────────────────────────────────────────────

    /// <summary>
    /// Loads one or more manifest paths (files or directories). Directories are expanded
    /// by globbing <c>*.yaml</c> and <c>*.yml</c>. All documents must agree on
    /// <c>version</c> and <c>namespace</c>; duplicate resource <c>id</c>s across files
    /// are an error. Returns a merged <see cref="ManifestFile"/>.
    /// </summary>
    public ManifestFile LoadFromPaths(IEnumerable<string> paths, Func<string, string?>? getVar = null)
    {
        var resolved = ExpandPaths(paths).ToList();

        if (resolved.Count == 0)
        {
            throw new ManifestException("No manifest files found in the specified path(s).");
        }

        if (resolved.Count == 1)
        {
            return LoadFromFile(resolved[0], getVar);
        }

        var manifests = resolved
            .Select(p => (Path: p, Manifest: LoadFromFile(p, getVar)))
            .ToList();

        return Merge(manifests);
    }

    // ── Deprecation check (called by callers that want the warning) ───────────────

    /// <summary>
    /// Returns true when the manifest is v1 (legacy, jobs-only). Callers should print a
    /// deprecation notice and recommend <c>steadycron export</c> to upgrade.
    /// </summary>
    public static bool IsV1(ManifestFile manifest) =>
        manifest.Version.HasValue && manifest.Version.Value == 1;

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Expands the given file/directory paths into a flat list of manifest files. Directories are
    /// globbed for <c>*.yaml</c>/<c>*.yml</c>; non-directory paths pass through verbatim. Shared with
    /// the env-file guardrail so it scans exactly the files the loader will read.
    /// </summary>
    public static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var yamlFiles = Directory.EnumerateFiles(path, "*.yaml")
                    .Concat(Directory.EnumerateFiles(path, "*.yml"))
                    .OrderBy(f => f, StringComparer.Ordinal);

                foreach (var file in yamlFiles)
                {
                    yield return file;
                }
            }
            else
            {
                yield return path;
            }
        }
    }

    private static ManifestFile Merge(List<(string Path, ManifestFile Manifest)> docs)
    {
        var first = docs[0].Manifest;
        var merged = new ManifestFile
        {
            Version = first.Version,
            Namespace = first.Namespace,
        };

        // Cross-file agreement checks
        for (var i = 1; i < docs.Count; i++)
        {
            var (path, m) = docs[i];
            if (m.Version.HasValue && first.Version.HasValue && m.Version != first.Version)
            {
                throw new ManifestException(
                    $"All manifest files must agree on 'version'. '{docs[0].Path}' has version {first.Version}, " +
                    $"but '{path}' has version {m.Version}.");
            }

            var effectiveNs = m.Namespace ?? merged.Namespace;
            if (merged.Namespace is not null && effectiveNs is not null &&
                !string.Equals(merged.Namespace, effectiveNs, StringComparison.Ordinal))
            {
                throw new ManifestException(
                    $"All manifest files must agree on 'namespace'. Found '{merged.Namespace}' and '{effectiveNs}' " +
                    $"in '{docs[0].Path}' and '{path}'.");
            }
        }

        // Collect all resources, checking for duplicate ids
        var jobIds = new Dictionary<string, string>(StringComparer.Ordinal);       // id → source file
        var channelIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var tagIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var variableIds = new Dictionary<string, string>(StringComparer.Ordinal);

        var jobs = new List<ManifestJob>();
        var channels = new List<ManifestChannel>();
        var tags = new List<ManifestTag>();
        var variables = new List<ManifestVariable>();

        foreach (var (path, m) in docs)
        {
            MergeList(m.Jobs, jobs, path, jobIds, j => j.Id);
            MergeList(m.Channels, channels, path, channelIds, c => c.Id);
            MergeList(m.Tags, tags, path, tagIds, t => t.Id);
            MergeList(m.Variables, variables, path, variableIds, v => v.Id);

            if (merged.Shaping is null && m.Shaping is not null)
            {
                merged.Shaping = m.Shaping;
            }
        }

        merged.Jobs = jobs;
        merged.Channels = channels.Count > 0 ? channels : null;
        merged.Tags = tags.Count > 0 ? tags : null;
        merged.Variables = variables.Count > 0 ? variables : null;

        return merged;
    }

    private static void MergeList<T>(
        List<T>? source,
        List<T> target,
        string sourcePath,
        Dictionary<string, string> seen,
        Func<T, string?> getId)
    {
        if (source is null) { return; }
        foreach (var item in source)
        {
            var id = getId(item);
            if (id is not null)
            {
                if (seen.TryGetValue(id, out var existingPath))
                {
                    throw new ManifestException(
                        $"Duplicate resource id '{id}' found in both '{existingPath}' and '{sourcePath}'. " +
                        $"Resource ids must be unique across all manifest files.");
                }

                seen[id] = sourcePath;
            }

            target.Add(item);
        }
    }
}
