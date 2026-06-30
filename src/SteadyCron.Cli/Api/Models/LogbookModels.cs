using System.Text.Json;

namespace SteadyCron.Cli.Api.Models;

public sealed record LogbookEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    string EventType,
    string Severity,
    Guid? JobId,
    string? JobName,
    string? Detail,
    Dictionary<string, JsonElement>? Metadata);

public sealed record LogbookResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<LogbookEntry> Items);
