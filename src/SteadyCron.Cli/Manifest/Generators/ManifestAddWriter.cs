namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>
/// The shared "insert, validate" tail of every <c>manifest add</c> command (SPEC-21 §3.1 steps
/// 4-5). Builds the candidate file content in memory and validates it before anything ever
/// touches disk — functionally the same guarantee as a copy-to-temp-then-replace, without the
/// extra file I/O and permission bookkeeping a real temp file would need. Validation resolves
/// every <c>${ENV}</c> placeholder to a dummy value: this is a structural/schema lint (does the
/// new block parse, does it collide with an existing resource's cross-references), not a real
/// interpolation pass — a resource that was just added can't have its secret in the environment yet.
/// </summary>
public sealed class ManifestAddWriter
{
    private readonly IManifestFileEditor _editor;
    private readonly ManifestLoader _loader;
    private readonly ManifestValidator _validator;

    public ManifestAddWriter(IManifestFileEditor editor, ManifestLoader loader, ManifestValidator validator)
    {
        _editor = editor;
        _loader = loader;
        _validator = validator;
    }

    /// <summary>
    /// Inserts <paramref name="block"/> into <paramref name="existingContent"/> and validates the
    /// result. On success, returns the candidate text to write and no errors. On failure (the
    /// file can't be safely edited, or the result doesn't validate), returns null and the
    /// error(s) — nothing is written by this method either way.
    /// </summary>
    public (string? Candidate, IReadOnlyList<string> Errors) BuildAndValidate(
        string existingContent, ManifestSection section, string block)
    {
        string candidate;
        try
        {
            candidate = _editor.Insert(existingContent, section, block);
        }
        catch (ManifestEditException ex)
        {
            return (null, [ex.Message]);
        }

        ManifestFile manifest;
        try
        {
            manifest = _loader.Parse(candidate, "<manifest>", name => $"dummy-{name}");
        }
        catch (ManifestException ex)
        {
            return (null, [ex.Message]);
        }

        var result = _validator.Validate(manifest);
        return result.IsValid ? (candidate, []) : (null, result.Errors);
    }
}
