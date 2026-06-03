using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SteadyCron.Cli.Manifest;

/// <summary>Loads and parses a YAML manifest file into a <see cref="ManifestFile"/>.</summary>
public sealed class ManifestLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Reads and parses the manifest at <paramref name="path"/>.</summary>
    public ManifestFile LoadFromFile(string path)
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

        return Parse(text, path);
    }

    /// <summary>Parses manifest YAML text. <paramref name="source"/> is used only in error messages.</summary>
    public ManifestFile Parse(string text, string source = "<manifest>")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ManifestException($"Manifest '{source}' is empty.");
        }

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
}
