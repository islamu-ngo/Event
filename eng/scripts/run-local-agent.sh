#!/usr/bin/env bash
# Launch the isolated Development browser profile from one approved vault authority.
# Values stay in this process environment; the repository and worktree .env stay unchanged.

set +x
set -euo pipefail
umask 077

if [[ $# -gt 1 || ( $# -eq 1 && "$1" != "--no-build" ) ]]; then
    printf 'Usage: bash eng/scripts/run-local-agent.sh [--no-build]\n' >&2
    exit 2
fi

if [[ ${DOTNET_ENVIRONMENT:-Development} != Development ||
      ${ASPNETCORE_ENVIRONMENT:-Development} != Development ]]; then
    printf 'The local agent authority is Development-only.\n' >&2
    exit 4
fi

for command_name in base64 curl dotnet jq; do
    command -v "$command_name" >/dev/null || {
        printf 'Missing required local tool: %s\n' "$command_name" >&2
        exit 2
    }
done

cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.."
while IFS= read -r line; do
    value=${line#*=}
    value=${value:1}
    case "$line" in
        'Infisical:Url = '*) vault_url=$value ;;
        'Infisical:ProjectId = '*) project_id=$value ;;
        'Infisical:Environment = '*) vault_environment=$value ;;
        'Infisical:ClientId = '*) client_id=$value ;;
        'Infisical:ClientSecret = '*) client_secret=$value ;;
    esac
done < <(dotnet user-secrets list --project src/Explore.Secrets/Explore.Secrets.csproj)

for key in vault_url project_id vault_environment client_id client_secret; do
    [[ -n ${!key:-} ]] || {
        printf 'Missing approved Infisical bootstrap setting: %s\n' "$key" >&2
        exit 4
    }
done

client_id_json=$(printf '%s' "$client_id" | jq -Rs .)
client_secret_json=$(printf '%s' "$client_secret" | jq -Rs .)
access_token=$(
    printf '{"clientId":%s,"clientSecret":%s}' "$client_id_json" "$client_secret_json" |
        curl --silent --show-error --fail --ipv4 --max-time 20 \
            -H 'Content-Type: application/json' --data-binary @- \
            "${vault_url%/}/api/v1/auth/universal-auth/login" |
        jq -er '.accessToken // empty'
)

for path in api postgresql; do
    encoded_path=$(jq -nr --arg value "/$path" '$value|@uri')
    response=$(
        curl --silent --show-error --fail --ipv4 --max-time 20 \
            --config <(printf 'header = "Authorization: Bearer %s"\n' "$access_token") \
            "${vault_url%/}/api/v3/secrets/raw?workspaceId=$project_id&environment=$vault_environment&secretPath=$encoded_path&expandSecretReferences=true&recursive=true"
    )
    while IFS=$'\t' read -r key encoded; do
        case "$key" in
            POSTGRESQL_USERNAME|POSTGRESQL_PASSWORD|AGENT_BROWSER_REDIS_PASSWORD|\
            AGENT_BROWSER_PERSONA_PASSWORD|INSTANCE_BOOTSTRAP_LOCAL_PASSWORD|\
            AUTHENTICATION_LOCAL_JWT_KEY)
                value=$(printf '%s' "$encoded" | base64 -d)
                export "$key=$value"
                ;;
            PATH|LD_*|DYLD_*|DOTNET_*|ASPNETCORE_*|BASH_ENV|ENV|\
            SHELLOPTS|BASHOPTS|IFS|HOME|TMPDIR)
                printf 'Rejected unsafe vault environment key: %s\n' "$key" >&2
                exit 4
                ;;
        esac
    done < <(
        printf '%s' "$response" |
            jq -r '.secrets[]? | select(.secretKey|test("^[A-Z_][A-Z0-9_]*$")) | select(.secretValue|type=="string") | [.secretKey, (.secretValue|@base64)] | @tsv'
    )
done

# The shared Development vault may describe Keycloak. Profile topology is
# compiled and Local-only; only secret values are imported from that authority.
export SECRET_PROVIDER=Environment
export SecretProvider__Provider=Environment
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
export ISLAMU_ASPIRE_MODE=AgentBrowser
export AGENT_BROWSER_SEED_ENABLED=true
export AUTHENTICATION_PROVIDER=local
export AUTHORIZATION_PROVIDER=local
export DATABASE_PROVIDER=PostgreSql
export DATABASE_NAME=islamu_event_agent
export IDENTITY_DATABASE_TOPOLOGY=colocated
export DEPLOYMENT_MODE=multi_tenant
export CONFIGURATION_MANIFEST_MODE=Off

for key in POSTGRESQL_USERNAME POSTGRESQL_PASSWORD AGENT_BROWSER_REDIS_PASSWORD \
    AGENT_BROWSER_PERSONA_PASSWORD AUTHENTICATION_LOCAL_JWT_KEY; do
    [[ -n ${!key:-} ]] || {
        printf 'Missing approved agent secret: %s\n' "$key" >&2
        exit 4
    }
done

printf 'LOCAL_AGENT_AUTHORITY_READY\n'
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj \
    --configuration Release --launch-profile local-agent "$@" 2>&1 |
    while IFS= read -r line; do
        case "$line" in
            *secret_authority_unavailable*) printf 'LOCAL_AGENT_AUTHORITY_FAILURE\n' ;;
            *'Application started.'*|*'AppHost is running.'*)
                printf 'LOCAL_AGENT_APPHOST_STARTED\n' ;;
            *'Unhandled exception'*|*' error:'*) printf 'LOCAL_AGENT_STARTUP_ERROR\n' ;;
        esac
    done
