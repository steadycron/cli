namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>The manifest's top-level resource sections a generator can insert into.</summary>
public enum ManifestSection
{
    Jobs,
    Channels,
    Tags,
    Variables,
}

/// <summary>
/// Thrown when a manifest can't be safely, additively edited (tabs, a section written in flow
/// style) — the caller should fall back to <c>--dry-run</c> and a manual paste (SPEC-21 §5.4).
/// </summary>
public sealed class ManifestEditException(string message) : Exception(message);

/// <summary>
/// Append-only insertion into an existing manifest file's text — never a full parse-and-re-emit,
/// so comments and formatting outside the inserted block survive untouched (SPEC-21 D1). One
/// implementation per target format; <see cref="YamlSectionAppendEditor"/> today, an HCL
/// (top-level `resource` block, always EOF-appended) implementation could follow the same
/// interface without changing any caller (SPEC-21 §4.3).
/// </summary>
public interface IManifestFileEditor
{
    /// <summary>
    /// True if an item already exists in <paramref name="section"/> where every one of
    /// <paramref name="matchFields"/> matches (AND semantics across the given pairs — pass a
    /// single pair for an "id OR name" check called twice by the caller; pass both key+value for
    /// a tag's compound identity).
    /// </summary>
    bool ResourceExists(string content, ManifestSection section, params (string Field, string Value)[] matchFields);

    /// <summary>
    /// Inserts <paramref name="block"/> (as produced by an <see cref="IManifestBlockRenderer"/>,
    /// authored at a canonical 2-space indent) into <paramref name="section"/>, creating the
    /// section if absent. Re-indents the block to match the file's detected indent width. Every
    /// existing line is reproduced byte-identical; only new lines are added.
    /// </summary>
    /// <exception cref="ManifestEditException">The file can't be safely edited.</exception>
    string Insert(string content, ManifestSection section, string block);

    /// <summary>Minimal header for a brand-new manifest file (SPEC-21 D3).</summary>
    string EmptyFileHeader();
}
