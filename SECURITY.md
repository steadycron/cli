# Security Policy

## Reporting a vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

If you discover a security issue in the SteadyCron CLI, report it privately so we can fix it before
it is disclosed:

- Email **security@steadycron.com**, or
- Use GitHub's [private vulnerability reporting](https://github.com/steadycron/cli/security/advisories/new)
  (**Security → Report a vulnerability** on this repository).

Please include enough detail to reproduce the issue: the CLI version (`steadycron --version`), your
OS, the command you ran, and what you observed. A proof of concept is appreciated but not required.

We aim to acknowledge a report within **3 business days** and to ship a fix or mitigation for
confirmed issues as quickly as the severity warrants. We'll keep you updated on progress and credit
you in the release notes unless you'd prefer to remain anonymous.

## Supported versions

Security fixes are released against the **latest** published version on
[NuGet](https://www.nuget.org/packages/steadycron) and the
[GitHub Releases](https://github.com/steadycron/cli/releases) page. Please upgrade before reporting —
the issue may already be fixed.

## Handling of credentials

The CLI authenticates with a SteadyCron API key. A few notes on how it treats secrets, since they're
relevant to any report:

- The API key is read from the `--api-key` flag, the `STEADYCRON_API_KEY` environment variable, or
  the config file — in that order. Prefer the environment variable or a `--env-file` over committing
  keys anywhere.
- Manifest secret fields (alert-channel credentials, template-variable values) are exported as
  `${SC_…}` placeholders, never in plaintext. Never commit a resolved `.env` file.
- If you believe the CLI is logging, transmitting, or persisting a secret in a way it shouldn't,
  that is in scope — please report it.
