ABOUTME: Short runnable onboarding path for contributors and local evaluators.
ABOUTME: Links deeper docs instead of duplicating self-hosting, testing, and contribution guides.

# Getting Started

> **Audience:** Contributors | Evaluators | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-08-09
> **Source Anchors:** `global.json`, `Explore.AppHost/AppHost.cs`, `docker-compose.yml`, `docs/SELF_HOSTING.md`, `docs/TESTING.md`

Use this page for the shortest safe local path. For production-style hosting, start with [SELF_HOSTING.md](SELF_HOSTING.md) instead. For hosted guides and platform tutorials, visit the [Official Documentation](https://islamu.gitbook.io/islamu-event).

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | `10.0.302` or compatible SDK from `global.json` | Build and run the solution. |
| Docker / Docker Compose v2 | Current | Local Aspire infrastructure and self-hosting stack. |
| .NET Aspire CLI | Current compatible CLI | Preferred local development loop; `dotnet run --project Explore.AppHost` remains the fallback/IDE launch path. |

Install the Aspire CLI before using `aspire run`:

```bash
curl -sSL https://aspire.dev/install.sh | bash
# Alternative when .NET 10 global tools are available:
dotnet tool install -g Aspire.Cli
```

If your shell cannot find `aspire` after the .NET tool install, add `$HOME/.dotnet/tools` to `PATH`.

## Clone And Run

```bash
git clone https://github.com/islamu-ngo/Event.git
cd Event
cp .env.example .env
aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

That command starts the default full-local Aspire topology. Aspire builds the AppHost, starts local infrastructure, runs migrations, starts API and Blazor, and prints the dashboard URL. Press `Ctrl+C` to stop the interactive run.

Run the build separately before opening a PR or when diagnosing compile failures:

```bash
dotnet build --configuration Release --verbosity quiet
```

Build errors must be fixed before continuing. For recurring failures, use [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Local Secrets & Infrastructure Profiles

Local secrets come only from the repository-root `.env` file or the explicitly
selected Infisical authority. Copy `.env.example` to `.env`, keep it untracked,
and populate only the credentials required by the chosen profile. .NET User
Secrets and appsettings are not secret origins.

### Infisical Configuration (Maintainers Only)
`local-full` does not require Infisical because Aspire starts local infrastructure and explicitly selects Environment authority while clearing Infisical bootstrap identifiers. Populate required local credentials in `.env`; AppHost does not generate or hard-code them.

If using external infrastructure with `local-core` or `local-lite`, select
Infisical and populate its explicit bootstrap inputs in `.env`:
```dotenv
SECRET_PROVIDER=Infisical
INFISICAL_URL=https://example.com
INFISICAL_PROJECT_ID=
INFISICAL_ENV=dev
INFISICAL_CLIENT_ID=
INFISICAL_CLIENT_SECRET=
```

### Aspire Profiles

| Profile | Use When | Infrastructure Ownership |
|---|---|---|
| `local-full` | Contributor default, smoke checks, first clone | Aspire starts PostgreSQL, Redis, RabbitMQ, Mailpit, CockroachDB, Phase Two Keycloak, Cerbos, MinIO, Svix, Coop, Osprey, Prometheus, and Grafana locally. |
| `local-core` | Maintainers debugging data/cache issues | Aspire starts PostgreSQL, Redis, Mailpit, and migrations locally; Keycloak, Cerbos, storage, webhooks, and moderation providers come from the selected external authority. |
| `local-lite` | Maintainers on the fast daily loop | Aspire starts Mailpit, migrations, API, and Blazor; all infrastructure comes from the selected external authority. |

`local-full` uses persistent containers and named volumes so repeated runs do not recreate the database, Keycloak, Mailpit messages, MinIO, RabbitMQ, or observability data from scratch. Keycloak keeps stable local ports for OIDC browser cookies and callbacks.

Mailpit starts in every Aspire profile. SMTP uses `localhost:1025`, and the capture UI is `http://localhost:8025`. Development seeding applies those values when `email.smtp_host` is empty, so registration and other email workflows can use the normal SMTP path without sending real email.

Osprey starts in `local-full` with `ghcr.io/roostorg/osprey/osprey-coordinator:latest` plus a local `osprey-kafka` broker and `osprey.actions_input` topic for the coordinator action consumer. It exposes coordinator ports `19950` and `19951`; the application `Reporting:Osprey` HTTP adapter stays disabled unless you provide a compatible facade endpoint.

For more detailed information, see [SECRETS.md](SECRETS.md) and [CONFIGURATION.md](CONFIGURATION.md).

## Option A: Run The Aspire Development Loop

Aspire is the preferred local development loop because `Explore.AppHost` starts infrastructure, runs the migration service before API and Blazor, then wires API discovery into Blazor without hardcoded ports.

```bash
aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

The default mode is `FullLocal`, so contributors do not need to remember a launch-profile name. Use the Aspire dashboard output to find exact dynamic endpoints. Do not assume the AppHost uses the same ports as Docker Compose.

Background run and stop:

```bash
aspire start --apphost Explore.AppHost/Explore.AppHost.csproj
aspire stop --apphost Explore.AppHost/Explore.AppHost.csproj
```

Maintainer-only alternate modes:

```bash
ISLAMU_ASPIRE_MODE=LocalDataExternalPlatform aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
ISLAMU_ASPIRE_MODE=ExternalInfra aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

Launch profiles remain available for IDEs or older workflows:

```bash
dotnet run --project Explore.AppHost/Explore.AppHost.csproj --launch-profile local-full
dotnet run --project Explore.AppHost/Explore.AppHost.csproj --launch-profile local-default
dotnet run --project Explore.AppHost/Explore.AppHost.csproj --launch-profile local-core
dotnet run --project Explore.AppHost/Explore.AppHost.csproj --launch-profile local-lite
```

All four launch profiles expose API at `https://localhost:7039` and Blazor at `https://localhost:7177`. Prefer `aspire run --apphost Explore.AppHost/Explore.AppHost.csproj` for the contributor path because it is shorter and works from a clean checkout.

AppHost injects structured `Database__*` fields and the correct runtime or
migrator role into each process. Explicit `Database:*` values win. Infisical loads
primary database settings directly from `/database` with `DATABASE_*` keys. Raw
application connection strings are not a deployment input.

## Option B: Run The Compose Stack

Use Compose when you want the self-hosting topology locally:

```bash
cp .env.example .env
docker compose config
docker compose up -d postgres redis mailpit keycloak-db keycloak
docker compose run --rm event-migrationservice
docker compose up -d islamu-event-api islamu-event-ui
```

Keycloak starts without importing the repository sample realm. During setup,
configure deployment-owned runtime values, open the advanced operator panel,
inspect with fresh Keycloak administrator credentials, and explicitly create
an absent realm/clients if needed. Existing realms are never reconciled at
startup.

The repository Compose default is PostgreSQL. For SQLite, SQL Server, MariaDB,
or MySQL, set the structured `DATABASE_*` fields described in
[CONFIGURATION.md](CONFIGURATION.md#persistence-configuration), provide the
selected engine/file, and require MigrationService to finish before API start.
Use `EmailDispatchProcessor:Mode=HostedService` for every non-PostgreSQL
provider.

PostgreSQL and SQL Server use `DATABASE_SCHEMA` as their namespace and retain
clean table names such as `users`. SQLite, MariaDB, and MySQL force the fixed
`ie_` prefix, such as `ie_users`; use a separate SQLite file or MySQL-family
database per local instance instead of trying to configure a prefix.

Default Compose endpoints:

| Service | URL |
|---|---|
| Blazor BFF/UI | `http://localhost:7002` |
| API | `http://localhost:7039` |
| Mailpit | `http://localhost:8025` |

Swagger, Scalar, and OpenAPI JSON endpoints are mapped only for Development/Testing API runs. The Compose stack uses `ASPNETCORE_ENVIRONMENT=Production`; use [API.md](API.md) and [API_COOKBOOK.md](API_COOKBOOK.md) for API guidance unless you intentionally run the API in a development profile.

Optional profiles:

```bash
docker compose --profile storage up -d
docker compose --profile authz up -d
docker compose --profile webhooks up -d
docker compose --profile moderation up -d
docker compose --profile osprey up -d
```

`moderation` starts Coop. `osprey` starts `ghcr.io/roostorg/osprey/osprey-coordinator:latest` on coordinator ports `19950` and `19951`.

For setup-secret behavior, Keycloak, reverse proxy, migrations, storage, and backups, use [SELF_HOSTING.md](SELF_HOSTING.md).

## First Contribution Path

1. Read [FIRST_CONTRIBUTION.md](FIRST_CONTRIBUTION.md) for the shortest docs-only or small-bug PR path.
2. Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR.
3. Read the source-owned doc for your area from [docs/index.md](index.md).
4. Make the smallest safe change.
5. Record docs impact: `Updated`, `Not needed`, or `Deferred`.

## Validation

Run the build and affected test projects individually. Do not run solution-level `dotnet test`.

Minimum documentation/architecture check:

```bash
dotnet test --project Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
```

Typical related-project examples:

```bash
dotnet test --project Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category!=Runtime]" --minimum-expected-tests 1
dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet
```

When working on SMTP, EmailDispatch, or optional RabbitMQ dispatch, run the matching Docker/Testcontainers lane:

```bash
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=RabbitMQ]" --minimum-expected-tests 1
```

See [TESTING.md](TESTING.md) for the full per-project validation matrix, TUnit conventions, and runtime lane policy.

## Related

- [README.md](../README.md) — product overview and role-based docs map.
- [SELF_HOSTING.md](SELF_HOSTING.md) — production-style Compose topology and operator guidance.
- [CONFIGURATION.md](CONFIGURATION.md) — runtime settings and secret-provider mappings.
- [FIRST_CONTRIBUTION.md](FIRST_CONTRIBUTION.md) — first PR path.
- [CONTRIBUTING.md](CONTRIBUTING.md) — contribution workflow and PR checklist.
- [TESTING.md](TESTING.md) — test project roles and commands.
