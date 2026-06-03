using System.Net;

namespace SteadyCron.Cli.Api;

/// <summary>
/// Thrown when the API returns a non-success status. Carries the HTTP status and, when the body
/// followed the API's <c>{ "error": "...", "message": "..." }</c> shape, the parsed error code and
/// human message.
/// </summary>
public sealed class SteadyCronApiException : Exception
{
    public SteadyCronApiException(
        HttpStatusCode statusCode,
        string method,
        string path,
        string? errorCode,
        string message,
        string? rawBody)
        : base(message)
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        ErrorCode = errorCode;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    public string Path { get; }

    /// <summary>The machine-readable <c>error</c> code (e.g. <c>plan_job_limit_exceeded</c>), if present.</summary>
    public string? ErrorCode { get; }

    public string? RawBody { get; }

    public bool IsAuthError =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
