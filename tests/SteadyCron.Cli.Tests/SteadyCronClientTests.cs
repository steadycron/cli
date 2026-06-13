using System.Net;
using System.Text;
using System.Text.Json;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class SteadyCronClientTests
{
    [Fact]
    public async Task Create_sends_bearer_auth_and_snake_case_body_under_api_prefix()
    {
        HttpRequestMessage? captured = null;
        string? body = null;

        var handler = new StubHandler(async (req, ct) =>
        {
            captured = req;
            body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.Created, """{"id":"0192a4e0-0000-7000-8000-000000000001","name":"job","kind":"http","schedule_kind":"cron","timezone":"UTC"}""");
        });

        var client = new SteadyCronClient(new HttpClient(handler), "https://api.steadycron.com", "sc_secret");

        await client.CreateJobAsync(new CreateJobRequest
        {
            Name = "job",
            ScheduleKind = ScheduleKind.Cron,
            CronExpression = "0 9 * * 1",
            HttpUrl = "https://example.com",
            HttpMethod = "POST",
        });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://api.steadycron.com/api/jobs", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("sc_secret", captured.Headers.Authorization.Parameter);

        Assert.NotNull(body);
        Assert.Contains("\"schedule_kind\":\"cron\"", body);
        Assert.Contains("\"http_method\":\"POST\"", body);
    }

    [Fact]
    public async Task ListAllJobs_follows_pagination()
    {
        var page1 = """{"items":[{"id":"0192a4e0-0000-7000-8000-000000000001","name":"a","kind":"http","schedule_kind":"interval","timezone":"UTC"}],"total_count":2,"page":1,"page_size":1}""";
        var page2 = """{"items":[{"id":"0192a4e0-0000-7000-8000-000000000002","name":"b","kind":"http","schedule_kind":"interval","timezone":"UTC"}],"total_count":2,"page":2,"page_size":1}""";

        var handler = new StubHandler((req, _) =>
        {
            var isPage2 = req.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            return Task.FromResult(Json(HttpStatusCode.OK, isPage2 ? page2 : page1));
        });

        var client = new SteadyCronClient(new HttpClient(handler), "https://api.steadycron.com", "sc_secret");

        var all = await client.ListAllJobsAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("a", all[0].Name);
        Assert.Equal("b", all[1].Name);
    }

    [Fact]
    public async Task Error_response_is_mapped_to_exception_with_code()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(
            HttpStatusCode.UnprocessableEntity,
            """{"error":"plan_job_limit_exceeded","message":"Your plan allows a maximum of 5 HTTP jobs."}""")));

        var client = new SteadyCronClient(new HttpClient(handler), "https://api.steadycron.com", "sc_secret");

        var ex = await Assert.ThrowsAsync<SteadyCronApiException>(() =>
            client.CreateJobAsync(new CreateJobRequest { Name = "x", ScheduleKind = ScheduleKind.Interval, IntervalSeconds = 60 }));

        Assert.Equal("plan_job_limit_exceeded", ex.ErrorCode);
        Assert.Contains("maximum of 5", ex.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_is_flagged_as_auth_error()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, "")));
        var client = new SteadyCronClient(new HttpClient(handler), "https://api.steadycron.com", "sc_bad");

        var ex = await Assert.ThrowsAsync<SteadyCronApiException>(() => client.GetJobAsync(Guid.NewGuid()));
        Assert.True(ex.IsAuthError);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
