namespace SteadyCron.Cli.Output;

/// <summary>Process exit codes. Stable so scripts and CI can branch on them.</summary>
public static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>An unexpected/unclassified error.</summary>
    public const int Error = 1;

    /// <summary>
    /// The manifest could not be loaded or validated.
    /// Also used by <c>plan --detailed-exitcode</c> when drift is detected (Terraform-style).
    /// Without <c>--detailed-exitcode</c>, a plan with drift still exits 0.
    /// </summary>
    public const int ManifestError = 2;

    /// <summary>The API returned an error response.</summary>
    public const int ApiError = 3;

    /// <summary>Missing/invalid credentials or configuration (also 401/403).</summary>
    public const int AuthError = 4;

    /// <summary>
    /// The plan or apply reported <c>errors[]</c> (limit violations, namespace conflicts)
    /// or one or more per-resource failures.
    /// </summary>
    public const int PlanErrors = 5;

    /// <summary>The operation was cancelled (Ctrl+C).</summary>
    public const int Cancelled = 130;
}
