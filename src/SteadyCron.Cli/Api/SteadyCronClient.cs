using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Api;

/// <summary>
/// Typed client over the SteadyCron REST API. One instance is bound to a base URL and an API key.
/// All routes live under <c>/api</c> and authenticate with <c>Authorization: Bearer sc_…</c>.
/// </summary>
public sealed class SteadyCronClient
{
    private readonly HttpClient _http;

    public SteadyCronClient(HttpClient http, string baseUrl, string apiKey)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        // API-key requests are exempt from the CSRF header check, but sending it is harmless and
        // keeps parity with the dashboard's mutating requests.
        _http.DefaultRequestHeaders.Add("X-Requested-With", "SteadyCron");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"steadycron-cli/{CliVersion.Value}");
    }

    // ── Jobs ────────────────────────────────────────────────────────────────────

    /// <summary>Fetches a single page of jobs.</summary>
    public Task<JobListResponse> ListJobsAsync(
        string? status = null,
        string? kind = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var query = $"api/jobs?page={page}&pageSize={pageSize}";
        if (status is not null)
        {
            query += $"&status={Uri.EscapeDataString(status)}";
        }

        if (kind is not null)
        {
            query += $"&kind={Uri.EscapeDataString(kind)}";
        }

        return GetAsync<JobListResponse>(query, ct);
    }

    /// <summary>Fetches every job across all pages (used by <c>sync</c>).</summary>
    public async Task<IReadOnlyList<JobResponse>> ListAllJobsAsync(
        string? kind = null,
        CancellationToken ct = default)
    {
        const int pageSize = 100;
        var all = new List<JobResponse>();
        var page = 1;
        while (true)
        {
            var response = await ListJobsAsync(kind: kind, page: page, pageSize: pageSize, ct: ct);
            all.AddRange(response.Items);
            if (all.Count >= response.TotalCount || response.Items.Count == 0)
            {
                break;
            }

            page++;
        }

        return all;
    }

    public Task<JobResponse> GetJobAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<JobResponse>($"api/jobs/{id}", ct);

    public Task<JobResponse> CreateJobAsync(CreateJobRequest request, CancellationToken ct = default) =>
        SendJsonAsync<JobResponse>(HttpMethod.Post, "api/jobs", request, ct);

    public Task<JobResponse> UpdateJobAsync(Guid id, UpdateJobRequest request, CancellationToken ct = default) =>
        SendJsonAsync<JobResponse>(HttpMethod.Patch, $"api/jobs/{id}", request, ct);

    public Task DeleteJobAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/jobs/{id}", content: null, ct);

    public Task<JobResponse> PauseJobAsync(Guid id, CancellationToken ct = default) =>
        SendJsonAsync<JobResponse>(HttpMethod.Post, $"api/jobs/{id}/pause", body: null, ct);

    public Task<JobResponse> ResumeJobAsync(Guid id, CancellationToken ct = default) =>
        SendJsonAsync<JobResponse>(HttpMethod.Post, $"api/jobs/{id}/resume", body: null, ct);

    public Task RunNowAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/jobs/{id}/run-now", content: null, ct);

    public Task<ExecutionListResponse> ListExecutionsAsync(
        Guid id, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        GetAsync<ExecutionListResponse>($"api/jobs/{id}/executions?page={page}&pageSize={pageSize}", ct);

    // ── Reconcile ─────────────────────────────────────────────────────────────────

    public Task<ReconcileResponse> ReconcileAsync(ReconcileRequest request, CancellationToken ct = default) =>
        SendJsonAsync<ReconcileResponse>(HttpMethod.Post, "api/reconcile", request, ct);

    // ── Export ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exports the entire account as a v2 manifest. Returns the raw YAML/JSON text
    /// exactly as the server produces it.
    /// </summary>
    public Task<string> ExportAccountAsync(string? format = null, string? ns = null, CancellationToken ct = default)
    {
        var query = "api/export";
        var sep = '?';
        if (format is not null) { query += $"{sep}format={Uri.EscapeDataString(format)}"; sep = '&'; }
        if (ns is not null) { query += $"{sep}namespace={Uri.EscapeDataString(ns)}"; }
        return GetStringAsync(query, ct);
    }

    /// <summary>Exports all jobs as a v2 manifest.</summary>
    public Task<string> ExportJobsAsync(string? format = null, string? ns = null, CancellationToken ct = default)
    {
        var query = "api/export/jobs";
        var sep = '?';
        if (format is not null) { query += $"{sep}format={Uri.EscapeDataString(format)}"; sep = '&'; }
        if (ns is not null) { query += $"{sep}namespace={Uri.EscapeDataString(ns)}"; }
        return GetStringAsync(query, ct);
    }

    /// <summary>Exports a single job by id as a v2 manifest.</summary>
    public Task<string> ExportJobAsync(Guid id, string? format = null, string? ns = null, CancellationToken ct = default)
    {
        var query = $"api/jobs/{id}/export";
        var sep = '?';
        if (format is not null) { query += $"{sep}format={Uri.EscapeDataString(format)}"; sep = '&'; }
        if (ns is not null) { query += $"{sep}namespace={Uri.EscapeDataString(ns)}"; }
        return GetStringAsync(query, ct);
    }

    /// <summary>Exports the whole account as a Terraform HCL file with import blocks.</summary>
    public Task<string> ExportAccountTerraformAsync(CancellationToken ct = default) =>
        GetStringAsync("api/export/terraform", ct);

    /// <summary>Exports a single job as a Terraform HCL file with import block.</summary>
    public Task<string> ExportJobTerraformAsync(Guid id, CancellationToken ct = default) =>
        GetStringAsync($"api/jobs/{id}/export/terraform", ct);

    // ── Cron ──────────────────────────────────────────────────────────────────────

    public Task<CronPreviewResponse> PreviewCronAsync(CronPreviewRequest request, CancellationToken ct = default) =>
        SendJsonAsync<CronPreviewResponse>(HttpMethod.Post, "api/cron/preview", request, ct);

    // ── Tags ──────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TagResponse>> ListTagsAsync(CancellationToken ct = default) =>
        await GetAsync<List<TagResponse>>("api/tags", ct);

    public Task<TagResponse> CreateTagAsync(CreateTagRequest request, CancellationToken ct = default) =>
        SendJsonAsync<TagResponse>(HttpMethod.Post, "api/tags", request, ct);

    public Task DeleteTagAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/tags/{id}", content: null, ct);

    // ── Template variables ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TemplateVariableResponse>> ListVariablesAsync(CancellationToken ct = default) =>
        await GetAsync<List<TemplateVariableResponse>>("api/variables", ct);

    public Task<TemplateVariableResponse> CreateVariableAsync(CreateTemplateVariableRequest request, CancellationToken ct = default) =>
        SendJsonAsync<TemplateVariableResponse>(HttpMethod.Post, "api/variables", request, ct);

    public Task<TemplateVariableResponse> UpdateVariableAsync(Guid id, UpdateTemplateVariableRequest request, CancellationToken ct = default) =>
        SendJsonAsync<TemplateVariableResponse>(HttpMethod.Patch, $"api/variables/{id}", request, ct);

    public Task DeleteVariableAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/variables/{id}", content: null, ct);

    // ── Alert channels ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AlertChannelResponse>> ListChannelsAsync(CancellationToken ct = default) =>
        await GetAsync<List<AlertChannelResponse>>("api/alert-channels", ct);

    public Task<AlertChannelResponse> CreateChannelAsync(CreateAlertChannelRequest request, CancellationToken ct = default) =>
        SendJsonAsync<AlertChannelResponse>(HttpMethod.Post, "api/alert-channels", request, ct);

    public Task TestChannelAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/alert-channels/{id}/test", content: null, ct);

    public Task DeleteChannelAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/alert-channels/{id}", content: null, ct);

    // ── Reports ──────────────────────────────────────────────────────────────────────

    public Task<Models.ReportSummaryResponse> GetReportSummaryAsync(
        int hours,
        CancellationToken ct = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-hours);
        var query = $"api/reports/summary?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        return GetAsync<Models.ReportSummaryResponse>(query, ct);
    }

    // ── Logbook ──────────────────────────────────────────────────────────────────────

    public Task<Models.LogbookResponse> ListLogbookEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<string>? eventTypes = null,
        IReadOnlyList<string>? severities = null,
        Guid? jobId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var parts = new List<string>
        {
            $"from={Uri.EscapeDataString(from.ToString("O"))}",
            $"to={Uri.EscapeDataString(to.ToString("O"))}",
            $"page={page}",
            $"page_size={pageSize}",
        };
        if (eventTypes is { Count: > 0 })
        {
            foreach (var t in eventTypes) { parts.Add($"event_type={Uri.EscapeDataString(t)}"); }
        }

        if (severities is { Count: > 0 })
        {
            foreach (var s in severities) { parts.Add($"severity={Uri.EscapeDataString(s)}"); }
        }

        if (jobId.HasValue) { parts.Add($"job_id={jobId.Value}"); }
        return GetAsync<Models.LogbookResponse>($"api/logbook?{string.Join("&", parts)}", ct);
    }

    // ── Alert rules ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AlertRuleResponse>> ListRulesAsync(Guid jobId, CancellationToken ct = default) =>
        await GetAsync<List<AlertRuleResponse>>($"api/jobs/{jobId}/alert-rules", ct);

    public Task<AlertRuleResponse> CreateRuleAsync(Guid jobId, CreateAlertRuleRequest request, CancellationToken ct = default) =>
        SendJsonAsync<AlertRuleResponse>(HttpMethod.Post, $"api/jobs/{jobId}/alert-rules", request, ct);

    public Task DeleteRuleAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/alert-rules/{id}", content: null, ct);

    // ── transport helpers ──────────────────────────────────────────────────────────

    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        return await ReadAsync<T>(response, HttpMethod.Get, path, ct);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SteadyCronJson.Options);
        }

        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync<T>(response, method, path, ct);
    }

    private async Task SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, method, path, ct);
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response, HttpMethod method, string path, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, method, path, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new SteadyCronApiException(
                response.StatusCode, method.Method, path, null,
                "The API returned an empty body where data was expected.", body);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, SteadyCronJson.Options)
                   ?? throw new SteadyCronApiException(
                       response.StatusCode, method.Method, path, null,
                       "The API returned a null body where data was expected.", body);
        }
        catch (JsonException ex)
        {
            throw new SteadyCronApiException(
                response.StatusCode, method.Method, path, null,
                $"Could not parse the API response: {ex.Message}", body);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, HttpMethod method, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        var (code, message) = ParseError(raw);
        throw new SteadyCronApiException(
            response.StatusCode,
            method.Method,
            path,
            code,
            message ?? DefaultMessageFor(response.StatusCode),
            raw);
    }

    private static (string? Code, string? Message) ParseError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var code = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string DefaultMessageFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Unauthorized — check your API key (STEADYCRON_API_KEY).",
        HttpStatusCode.Forbidden => "Forbidden — your API key may be read-only or lack scope for this operation.",
        HttpStatusCode.NotFound => "Not found.",
        HttpStatusCode.TooManyRequests => "Rate limited — too many requests for this API key. Try again shortly.",
        _ => $"Request failed with status {(int)status} {status}.",
    };
}
