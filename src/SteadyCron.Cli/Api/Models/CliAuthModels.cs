namespace SteadyCron.Cli.Api.Models;

/// <summary>
/// Versioned response envelope every <c>/api/auth/cli/*</c> endpoint uses. Unknown/extra
/// properties (e.g. the dev-only <c>dev_code</c> field) are ignored by default, so this model
/// only needs the fields the CLI actually reads.
/// </summary>
public sealed record CliAuthEnvelope<TData>(int V, string Status, string? Error, string? Message, TData? Data);

public sealed record CliSignupData(Guid SignupId);

public sealed record CliProvisioningData(string ProvisioningToken, int ExpiresIn);

public sealed record CliProvisionKeyData(string ApiKey, string KeyPrefix, string AccountPlan);
