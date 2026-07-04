namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>
/// Format-agnostic result of collecting flags/prompts for <c>manifest add job</c> — no YAML types
/// or string fragments, so a future <c>HclBlockRenderer</c> can consume the exact same model.
/// </summary>
public sealed record NewJobSpec
{
    /// <summary><c>http</c> or <c>heartbeat</c>.</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    /// <summary>Stable reconciliation key. Always derived from <see cref="Name"/> — there is no
    /// separate prompt/flag for it (SPEC-21 §3.3).</summary>
    public required string Id { get; init; }

    /// <summary>Exactly one of <see cref="Schedule"/> / <see cref="Interval"/> is set.</summary>
    public string? Schedule { get; init; }

    public int? Interval { get; init; }

    public required string Timezone { get; init; }

    /// <summary>Heartbeat only.</summary>
    public int? Grace { get; init; }

    /// <summary>HTTP only.</summary>
    public string? Url { get; init; }

    /// <summary>HTTP only.</summary>
    public string? Method { get; init; }

    public bool IsHttp => string.Equals(Kind, "http", StringComparison.Ordinal);
}

/// <summary>Result of collecting flags/prompts for <c>manifest add channel</c>.</summary>
public sealed record NewChannelSpec
{
    /// <summary>email | slack | discord | webhook | telegram.</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    /// <summary>email only — the one kind whose config isn't secret-bearing, so it's the one
    /// value this command ever prompts for directly.</summary>
    public string? To { get; init; }
}

/// <summary>Result of collecting flags/prompts for <c>manifest add tag</c>.</summary>
public sealed record NewTagSpec
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public string? Color { get; init; }
}

/// <summary>Result of collecting flags/prompts for <c>manifest add variable</c>.</summary>
public sealed record NewVariableSpec
{
    public required string Name { get; init; }
}
