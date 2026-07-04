namespace SteadyCron.Cli.Infrastructure;

/// <summary>
/// Local (offline) timezone resolution/validation, shared by every interactive timezone prompt
/// (the <c>init</c> wizard and the <c>manifest add</c> generators) — the UTC/Local/Other selector
/// shape is the same everywhere; only how "Other" gets validated differs by context (server-side
/// cron preview for <c>init</c>, this class for anything that must work with no API key).
/// </summary>
public static class TimezoneHelper
{
    /// <summary>
    /// Resolves the local machine's IANA zone name, converting from a Windows id if necessary.
    /// Returns null when detection fails or resolves to UTC (nothing useful to offer as "Local").
    /// </summary>
    public static string? ResolveLocalIana()
    {
        try
        {
            var id = TimeZoneInfo.Local.Id;
            if (OperatingSystem.IsWindows())
            {
                if (!TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana))
                {
                    return null;
                }

                id = iana;
            }

            return string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase) ? null : id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the local system recognizes <paramref name="timezone"/> as a valid zone id.</summary>
    public static bool IsValid(string timezone)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
