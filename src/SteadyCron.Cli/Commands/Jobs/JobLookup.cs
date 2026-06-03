using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

/// <summary>Resolves a job from a user-supplied identifier that is either a job id (GUID) or a name.</summary>
internal static class JobLookup
{
    public static async Task<JobResponse> ResolveAsync(
        SteadyCronClient client, string identifier, CancellationToken ct)
    {
        if (Guid.TryParse(identifier, out var id))
        {
            return await client.GetJobAsync(id, ct);
        }

        var all = await client.ListAllJobsAsync(ct: ct);
        var matches = all.Where(j => string.Equals(j.Name, identifier, StringComparison.Ordinal)).ToList();

        return matches.Count switch
        {
            0 => throw new CliException($"No job found with name or id '{identifier}'.", ExitCodes.Error),
            1 => matches[0],
            _ => throw new CliException(
                $"{matches.Count} jobs are named '{identifier}'. Use the job id to disambiguate.",
                ExitCodes.Error),
        };
    }
}
