using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Generates stable <c>job_key</c> values for imported resources.
/// A slug is preferred when a display name is available; a hash is used as a fallback.
/// </summary>
public static class JobKeyGenerator
{
    private static readonly Regex NonAlphanumeric =
        new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Converts a human-readable name into a lowercase hyphen-slug suitable as a manifest id.
    /// E.g. "Send weekly digest!" → "send-weekly-digest", truncated to 64 chars.
    /// </summary>
    public static string ToSlug(string name)
    {
        var slug = NonAlphanumeric
            .Replace(name.ToLowerInvariant().Trim(), "-")
            .Trim('-');

        if (slug.Length == 0)
        {
            return "job";
        }

        return slug.Length > 64 ? slug[..64].TrimEnd('-') : slug;
    }

    /// <summary>
    /// Produces an 8-character lowercase hex string from the SHA-256 of <paramref name="rawLine"/>.
    /// Used as a deterministic fallback id when no name is available, so re-importing the
    /// same crontab line always yields the same key and <c>sync</c> is idempotent.
    /// </summary>
    public static string FromLine(string rawLine)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawLine));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
