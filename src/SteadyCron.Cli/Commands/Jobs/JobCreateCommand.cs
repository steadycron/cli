using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobCreateSettings : CliSettings
{
    [CommandOption("--name <NAME>")]
    [Description("Job name (unique within the account).")]
    public string? Name { get; set; }

    [CommandOption("--kind <KIND>")]
    [Description("http (default) or heartbeat.")]
    public string? Kind { get; set; }

    [CommandOption("--description <TEXT>")]
    public string? Description { get; set; }

    [CommandOption("--schedule <CRON>")]
    [Description("Cron expression, e.g. \"0 9 * * 1\". Mutually exclusive with --interval.")]
    public string? Schedule { get; set; }

    [CommandOption("--interval <SECONDS>")]
    [Description("Interval in seconds (10–86400). Mutually exclusive with --schedule.")]
    public int? Interval { get; set; }

    [CommandOption("--timezone <TZ>")]
    [Description("IANA timezone (default UTC).")]
    public string? Timezone { get; set; }

    [CommandOption("--paused")]
    [Description("Create the job in a paused state.")]
    public bool Paused { get; set; }

    // HTTP
    [CommandOption("--method <METHOD>")]
    [Description("http: GET (default), POST, PUT, PATCH, DELETE.")]
    public string? Method { get; set; }

    [CommandOption("--url <URL>")]
    [Description("http: target URL (required for HTTP jobs).")]
    public string? Url { get; set; }

    [CommandOption("--header <HEADER>")]
    [Description("http: request header as 'Name: value' (repeatable).")]
    public string[] Headers { get; set; } = [];

    [CommandOption("--body <BODY>")]
    [Description("http: request body.")]
    public string? Body { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("http: request timeout in seconds.")]
    public int? Timeout { get; set; }

    [CommandOption("--retries <N>")]
    [Description("http: max retries.")]
    public int? Retries { get; set; }

    [CommandOption("--retry-backoff <SECONDS>")]
    [Description("http: backoff between retries in seconds.")]
    public int? RetryBackoff { get; set; }

    [CommandOption("--no-retry-on-timeout")]
    [Description("http: do not retry on timeout (retries on timeout by default).")]
    public bool NoRetryOnTimeout { get; set; }

    [CommandOption("--retry-on-status <CODES>")]
    [Description("http: HTTP status codes to retry on (repeatable or comma-separated).")]
    public string[] RetryOnStatus { get; set; } = [];

    [CommandOption("--skip-if-running")]
    [Description("http: skip a fire if the previous run is still in flight.")]
    public bool SkipIfRunning { get; set; }

    [CommandOption("--misfire <POLICY>")]
    [Description("http: do_nothing (default) or fire_once_now.")]
    public string? MisfirePolicy { get; set; }

    // Heartbeat
    [CommandOption("--grace <SECONDS>")]
    [Description("heartbeat: grace period before a missed ping alerts.")]
    public int? Grace { get; set; }

    [CommandOption("--no-stuck-run-detection")]
    [Description("heartbeat: disable stuck-run detection (enabled by default).")]
    public bool NoStuckRunDetection { get; set; }

    [CommandOption("--max-run-duration <SECONDS>")]
    [Description("heartbeat: max in-flight run duration before a /start is abandoned.")]
    public int? MaxRunDuration { get; set; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Name)
            ? ValidationResult.Error("--name is required.")
            : ValidationResult.Success();
}

/// <summary>`steadycron jobs create` — creates a single job from flags (validated via the same
/// rules as the manifest).</summary>
public sealed class JobCreateCommand : SteadyCronCommandBase<JobCreateSettings>
{
    private readonly JobMapper _mapper;

    public JobCreateCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c, JobMapper mapper)
        : base(r, f, c)
    {
        _mapper = mapper;
    }

    protected override async Task<int> RunAsync(JobCreateSettings settings, OutputContext output, CancellationToken ct)
    {
        var manifestJob = ToManifestJob(settings);

        // Reuse the manifest mapper for validation + API defaults, so `jobs create` and `sync`
        // are guaranteed to behave identically.
        var desired = _mapper.ToDesired(manifestJob, 0);
        var request = _mapper.ToCreateRequest(desired);

        var client = CreateClient(settings);
        var job = await client.CreateJobAsync(request, ct);

        if (output.Json)
        {
            output.WriteJson(job);
            return ExitCodes.Ok;
        }

        output.Success($"Created {job.Kind} job '{job.Name}' ({job.Id}).");
        if (job.PingUrls is { } ping)
        {
            output.Line();
            output.Markup($"  [grey]ping (success):[/] {OutputContext.Escape(ping.Success)}");
            output.Markup($"  [grey]ping (start):[/]   {OutputContext.Escape(ping.Start)}");
            output.Markup($"  [grey]ping (fail):[/]    {OutputContext.Escape(ping.Fail)}");
        }

        if (job.PausedReason == JobFormatting.UnverifiedEmailPauseReason)
        {
            output.Warn(JobFormatting.UnverifiedEmailWarning);
        }

        return ExitCodes.Ok;
    }

    private static ManifestJob ToManifestJob(JobCreateSettings s)
    {
        var job = new ManifestJob
        {
            Name = s.Name,
            Kind = s.Kind,
            Description = s.Description,
            Schedule = s.Schedule,
            Interval = s.Interval,
            Timezone = s.Timezone,
            Paused = s.Paused ? true : null,
            Method = s.Method,
            Url = s.Url,
            Body = s.Body,
            Timeout = s.Timeout,
            Retries = s.Retries,
            RetryBackoff = s.RetryBackoff,
            RetryOnTimeout = s.NoRetryOnTimeout ? false : null,
            SkipIfRunning = s.SkipIfRunning ? true : null,
            MisfirePolicy = s.MisfirePolicy,
            Grace = s.Grace,
            StuckRunDetection = s.NoStuckRunDetection ? false : null,
            MaxRunDuration = s.MaxRunDuration,
        };

        if (s.Headers.Length > 0)
        {
            job.Headers = ParseHeaders(s.Headers);
        }

        var codes = ParseStatusCodes(s.RetryOnStatus);
        if (codes.Count > 0)
        {
            job.RetryOnStatus = codes;
        }

        return job;
    }

    private static Dictionary<string, string> ParseHeaders(IEnumerable<string> raw)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in raw)
        {
            var idx = entry.IndexOf(':');
            if (idx <= 0)
            {
                throw new CliException($"Invalid --header '{entry}'. Use 'Name: value'.", ExitCodes.Error);
            }

            headers[entry[..idx].Trim()] = entry[(idx + 1)..].Trim();
        }

        return headers;
    }

    private static List<int> ParseStatusCodes(IEnumerable<string> raw)
    {
        var codes = new List<int>();
        foreach (var token in raw.SelectMany(r => r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!int.TryParse(token, out var code))
            {
                throw new CliException($"Invalid --retry-on-status value '{token}'.", ExitCodes.Error);
            }

            codes.Add(code);
        }

        return codes;
    }
}
