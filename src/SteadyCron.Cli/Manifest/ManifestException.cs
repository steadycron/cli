namespace SteadyCron.Cli.Manifest;

/// <summary>Raised for any problem loading or validating a manifest (parse errors, bad fields).</summary>
public sealed class ManifestException : Exception
{
    public ManifestException(string message)
        : base(message)
    {
    }

    public ManifestException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
