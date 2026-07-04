using Cronos;

namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>
/// Local (offline) cron parsing and next-fire computation via Cronos, so <c>manifest add</c> can
/// offer the same schedule-derived grace default and next-fires preview as the <c>init</c>
/// wizard without any network access or API key.
/// </summary>
public static class CronScheduleHelper
{
    /// <summary>Parses/validates a 5-field cron expression. Throws <see cref="FormatException"/>
    /// with a friendly message on invalid syntax.</summary>
    public static void Validate(string expression)
    {
        try
        {
            CronExpression.Parse(expression.Trim());
        }
        catch (CronFormatException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Computes the next <paramref name="count"/> fire times for a cron expression in the given
    /// IANA timezone. Assumes <paramref name="expression"/> already passed <see cref="Validate"/>
    /// and <paramref name="timezone"/> already passed <see cref="Infrastructure.TimezoneHelper.IsValid"/>.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> GetNextFires(string expression, string timezone, int count)
    {
        var expr = CronExpression.Parse(expression.Trim());
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var from = DateTime.UtcNow;

        return expr.GetOccurrences(from, from.AddYears(2), tz, fromInclusive: false)
            .Take(count)
            .Select(d => new DateTimeOffset(d, TimeSpan.Zero))
            .ToList();
    }

    /// <summary>SPEC-20c §3.5 / SPEC-21 §3.3: grace defaults from the tightest gap between the
    /// next few fires, rather than a flat 1800s for every schedule.</summary>
    public static int ComputeGraceDefault(IReadOnlyList<DateTimeOffset> fires)
    {
        if (fires.Count < 2)
        {
            return 1800;
        }

        var minGap = TimeSpan.MaxValue;
        for (var i = 1; i < fires.Count; i++)
        {
            var gap = fires[i] - fires[i - 1];
            if (gap < minGap)
            {
                minGap = gap;
            }
        }

        if (minGap < TimeSpan.FromHours(1))
        {
            return 300;
        }

        return minGap < TimeSpan.FromHours(24) ? 1800 : 3600;
    }
}
