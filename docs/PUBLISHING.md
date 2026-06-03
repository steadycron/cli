# Publishing the SteadyCron CLI to NuGet

This is the maintainer guide for releasing the `steadycron` .NET global tool to
[nuget.org](https://www.nuget.org) and producing the GitHub Release binaries.

## One-time setup

1. **NuGet.org account**
   - Sign in at <https://www.nuget.org> (Microsoft/GitHub account).
   - Enable **two-factor authentication** (Account settings). nuget.org increasingly expects 2FA on
     publishing accounts.

2. **Confirm the package id is free**
   - `steadycron` is currently **available** (verified). Don't let anyone squat it — publish a
     `1.0.0` (or a `0.x` placeholder) early to claim it.

3. **Create a scoped API key** (Account → API Keys → Create):
   - **Key name:** `steadycron-cli-ci`
   - **Scopes:** *Push* → *Push new packages and package versions*
   - **Glob pattern:** `steadycron*` (or exactly `steadycron`)
   - **Expiration:** 365 days (rotate before it lapses)
   - Copy the key now — it is shown once.

4. **Add the key to GitHub Actions**
   - Repo → Settings → Secrets and variables → Actions → New repository secret
   - **Name:** `NUGET_API_KEY`  **Value:** the key from step 3
   - The [release workflow](../.github/workflows/release.yml) reads `secrets.NUGET_API_KEY`.

5. **(Optional, after first publish) Reserve the id prefix**
   - Apply for [ID prefix reservation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation)
     for `SteadyCron.*` (and/or `steadycron`). A reserved prefix gives the package the blue
     "verified owner" check and blocks look-alikes — worth it for an official tool.

6. **(Optional) Package icon**
   - Drop a square PNG (128×128 recommended, < 1 MB) at the repo root named `icon.png`.
   - The project auto-includes it (`<PackageIcon>`); no further changes needed.

## Cutting a release (recommended: tag-driven CI)

The version comes from the git tag (`vX.Y.Z` → package version `X.Y.Z`).

```bash
# 1. Update the changelog (move Unreleased → the new version) and commit.
git add CHANGELOG.md && git commit -m "Release 1.0.0"

# 2. Tag and push — this triggers .github/workflows/release.yml
git tag v1.0.0
git push origin main --tags
```

The workflow then:
1. runs the tests,
2. packs the tool and pushes it to nuget.org (`--skip-duplicate`),
3. builds self-contained single-file binaries for 6 RIDs,
4. creates the GitHub Release with the binaries and the `.nupkg` attached.

## Manual release (fallback, no CI)

```bash
dotnet test -c Release
dotnet pack src/SteadyCron.Cli/SteadyCron.Cli.csproj -c Release -p:Version=1.0.0 -o artifacts
dotnet nuget push "artifacts/steadycron.1.0.0.nupkg" \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate
```

## Verify after publishing

NuGet validation + indexing takes a few minutes. Then:

```bash
dotnet tool install -g steadycron --version 1.0.0
steadycron --version
```

## Versioning

[Semantic Versioning](https://semver.org). Pre-1.0 (`0.x`) signals an unstable API; cut `1.0.0`
when the command surface is considered stable. Keep [CHANGELOG.md](../CHANGELOG.md) current.
