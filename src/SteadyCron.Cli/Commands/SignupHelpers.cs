using System.Text.Json;
using Spectre.Console;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

/// <summary>
/// Shared orchestration between <see cref="SignupCommand"/> and <see cref="LoginCommand"/>: the
/// code-entry retry loop and the provision-key-then-save-config step are identical once a
/// <c>signup_id</c> is in hand, regardless of which command got there.
/// </summary>
internal static class SignupHelpers
{
    public static async Task<CliSignupData> SignupOrSuggestLoginAsync(
        SteadyCronClient client, string email, string password, CancellationToken ct)
    {
        try
        {
            return await client.CliSignupAsync(email, password, ct);
        }
        catch (SteadyCronApiException ex) when (ex.ErrorCode == "email_in_use")
        {
            throw new CliException($"{ex.Message} Try `steadycron login` instead.", ExitCodes.Error);
        }
    }

    /// <summary>Prompts for the code, retrying on wrong-guess and offering resend once locked.</summary>
    public static async Task<CliProvisioningData> VerifyLoopAsync(
        SteadyCronClient client, Guid signupId, OutputContext output, CancellationToken ct)
    {
        while (true)
        {
            var code = output.Out.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Enter 6-digit code:")));

            try
            {
                return await client.CliVerifyAsync(signupId, code, ct);
            }
            catch (SteadyCronApiException ex) when (ex.ErrorCode == "invalid_code")
            {
                output.Warn(ex.Message);
            }
            catch (SteadyCronApiException ex) when (ex.ErrorCode == "code_expired_or_locked")
            {
                output.Warn(ex.Message);

                var choice = output.Out.Prompt(
                    new SelectionPrompt<string>()
                        .Title(PromptFormatting.Marker("What next?"))
                        .AddChoices("Resend code", "Quit"));

                if (choice == "Quit")
                {
                    throw new CliException("Cancelled.", ExitCodes.Cancelled);
                }

                await client.CliResendAsync(signupId, ct);
                output.Success("A new code has been sent.");
            }
            catch (SteadyCronApiException ex) when (ex.ErrorCode == "already_verified")
            {
                throw new CliException(
                    "This email is already verified. Try `steadycron login` instead.", ExitCodes.Error);
            }
        }
    }

    /// <summary>Mints the API key from a provisioning token and writes it to the config file.</summary>
    public static async Task ProvisionAndSaveAsync(
        SteadyCronClient client,
        CliConfiguration config,
        CliSettings settings,
        string provisioningToken,
        OutputContext output,
        CancellationToken ct)
    {
        var keyName = $"cli-{Environment.MachineName}";
        var provisioned = await client.CliProvisionKeyAsync(provisioningToken, keyName, ct);

        var path = ConfigFile.ResolvePath();
        var existing = ConfigFile.Load(path);
        var merged = new ConfigFile
        {
            ApiUrl = string.IsNullOrWhiteSpace(settings.ApiUrl)
                ? (existing?.ApiUrl ?? config.ApiUrl)
                : settings.ApiUrl.Trim().TrimEnd('/'),
            ApiKey = provisioned.ApiKey,
        };
        merged.Save(path);

        // Never echo the full key — only its masked prefix, matching `config show`'s convention.
        output.Success($"API key created ({keyName}, scope: full) and saved to {ConfigFile.Abbreviate(path)}.");
    }

    /// <summary>Recovers <c>data.signup_id</c> from a 403 <c>email_not_verified</c> error body.</summary>
    public static Guid ExtractSignupId(SteadyCronApiException ex)
    {
        if (ex.RawBody is null)
        {
            throw new CliException("Unexpected response from the server.", ExitCodes.ApiError);
        }

        using var doc = JsonDocument.Parse(ex.RawBody);
        return doc.RootElement.GetProperty("data").GetProperty("signup_id").GetGuid();
    }
}
