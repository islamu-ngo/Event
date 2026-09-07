#!/usr/bin/env bash
set -euo pipefail

KEYCLOAK_INTERNAL_URL="${KEYCLOAK_INTERNAL_URL:-http://keycloak:8080}"
KEYCLOAK_REALM="${KEYCLOAK_REALM:-ISLAMU}"
KEYCLOAK_ADMIN="${KEYCLOAK_ADMIN:-}"
KEYCLOAK_ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-}"
KEYCLOAK_BLAZOR_CLIENT_ID="${KEYCLOAK_BLAZOR_CLIENT_ID:-islamu-event-blazor}"
KEYCLOAK_API_CLIENT_ID="${KEYCLOAK_API_CLIENT_ID:-islamu-event-api}"
KEYCLOAK_SMTP_HOST="${KEYCLOAK_SMTP_HOST:-}"
KEYCLOAK_SMTP_PORT="${KEYCLOAK_SMTP_PORT:-}"
KEYCLOAK_SMTP_FROM="${KEYCLOAK_SMTP_FROM:-}"
KEYCLOAK_SMTP_FROM_DISPLAY_NAME="${KEYCLOAK_SMTP_FROM_DISPLAY_NAME:-}"
KEYCLOAK_SMTP_AUTH="${KEYCLOAK_SMTP_AUTH:-false}"
KEYCLOAK_SMTP_SSL="${KEYCLOAK_SMTP_SSL:-false}"
KEYCLOAK_SMTP_STARTTLS="${KEYCLOAK_SMTP_STARTTLS:-false}"
KEYCLOAK_SMTP_REPLY_TO="${KEYCLOAK_SMTP_REPLY_TO:-}"
KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME="${KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME:-}"
KEYCLOAK_SMTP_ENVELOPE_FROM="${KEYCLOAK_SMTP_ENVELOPE_FROM:-}"
KEYCLOAK_SMTP_USER="${KEYCLOAK_SMTP_USER:-}"
KEYCLOAK_SMTP_PASSWORD="${KEYCLOAK_SMTP_PASSWORD:-}"
KCADM="${KCADM:-/opt/keycloak/bin/kcadm.sh}"
BLAZOR_REDIRECT_URIS="${KEYCLOAK_BLAZOR_REDIRECT_URIS:-[\"http://localhost:7002/signin-oidc\",\"http://admin.localhost:7002/signin-oidc\",\"https://localhost:7177/signin-oidc\",\"https://admin.localhost:7177/signin-oidc\"]}"
BLAZOR_WEB_ORIGINS="${KEYCLOAK_BLAZOR_WEB_ORIGINS:-[\"http://localhost:7002\",\"http://admin.localhost:7002\",\"https://localhost:7177\",\"https://admin.localhost:7177\"]}"
BLAZOR_LOGOUT_REDIRECT_URIS="${KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS:-http://localhost:7002/signout-callback-oidc##http://admin.localhost:7002/signout-callback-oidc##https://localhost:7177/signout-callback-oidc##https://admin.localhost:7177/signout-callback-oidc}"

log() {
  printf '[keycloak-init] %s\n' "$1" >&2
}

fail() {
  log "ERROR: $1"
  exit 1
}

require_non_empty() {
  local name="$1"
  local value="$2"

  if [ -z "$value" ]; then
    fail "$name is required. Set it through environment/secret-provider configuration."
  fi
}

json_escape() {
  local value="${1//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '%s' "$value"
}

resolve_blazor_secret() {
  require_non_empty "KEYCLOAK_BLAZOR_CLIENT_SECRET" "${KEYCLOAK_BLAZOR_CLIENT_SECRET:-}"
  printf '%s' "$KEYCLOAK_BLAZOR_CLIENT_SECRET"
}

client_uuid() {
  local client_id="$1"
  "$KCADM" get clients -r "$KEYCLOAK_REALM" -q "clientId=$client_id" --fields id --format csv --noquotes | head -n 1 | tr -d '\r'
}

set_client_secret() {
  local client_id="$1"
  local secret="$2"
  local uuid

  require_non_empty "secret for client $client_id" "$secret"

  uuid="$(client_uuid "$client_id")"
  if [ -z "$uuid" ]; then
    fail "Keycloak client '$client_id' was not found in realm '$KEYCLOAK_REALM'. Confirm realm import completed successfully."
  fi

  "$KCADM" update "clients/$uuid" -r "$KEYCLOAK_REALM" -s "secret=$secret" >/dev/null
  log "Synchronized secret for client '$client_id' (value redacted)."
}

sync_realm_security_settings() {
  "$KCADM" update "realms/$KEYCLOAK_REALM" \
    -s sslRequired=external \
    -s registrationAllowed=true \
    -s verifyEmail=true \
    -s 'passwordPolicy=length(12) and notUsername and notEmail and passwordHistory(5)' \
    -s ssoSessionIdleTimeout=1800 \
    -s ssoSessionMaxLifespan=36000 \
    -s ssoSessionIdleTimeoutRememberMe=2592000 \
    -s ssoSessionMaxLifespanRememberMe=7776000 \
    -s offlineSessionIdleTimeout=2592000 \
    -s offlineSessionMaxLifespan=7776000 \
    -s offlineSessionMaxLifespanEnabled=true >/dev/null

  log "Synchronized TLS, registration, password, and session policies for realm '$KEYCLOAK_REALM'."
}

sync_realm_smtp_settings() {
  local smtp_server

  if [ -z "$KEYCLOAK_SMTP_HOST$KEYCLOAK_SMTP_PORT$KEYCLOAK_SMTP_FROM" ]; then
    log "KEYCLOAK_SMTP_* is unset; leaving realm SMTP settings unchanged."
    return 0
  fi

  require_non_empty "KEYCLOAK_SMTP_HOST" "$KEYCLOAK_SMTP_HOST"
  require_non_empty "KEYCLOAK_SMTP_PORT" "$KEYCLOAK_SMTP_PORT"
  require_non_empty "KEYCLOAK_SMTP_FROM" "$KEYCLOAK_SMTP_FROM"

  smtp_server="{\"host\":\"$(json_escape "$KEYCLOAK_SMTP_HOST")\",\"port\":\"$(json_escape "$KEYCLOAK_SMTP_PORT")\",\"from\":\"$(json_escape "$KEYCLOAK_SMTP_FROM")\",\"fromDisplayName\":\"$(json_escape "$KEYCLOAK_SMTP_FROM_DISPLAY_NAME")\",\"auth\":\"$(json_escape "$KEYCLOAK_SMTP_AUTH")\",\"ssl\":\"$(json_escape "$KEYCLOAK_SMTP_SSL")\",\"starttls\":\"$(json_escape "$KEYCLOAK_SMTP_STARTTLS")\""

  if [ -n "$KEYCLOAK_SMTP_REPLY_TO" ]; then
    smtp_server="$smtp_server,\"replyTo\":\"$(json_escape "$KEYCLOAK_SMTP_REPLY_TO")\""
  fi
  if [ -n "$KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME" ]; then
    smtp_server="$smtp_server,\"replyToDisplayName\":\"$(json_escape "$KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME")\""
  fi
  if [ -n "$KEYCLOAK_SMTP_ENVELOPE_FROM" ]; then
    smtp_server="$smtp_server,\"envelopeFrom\":\"$(json_escape "$KEYCLOAK_SMTP_ENVELOPE_FROM")\""
  fi
  if [ -n "$KEYCLOAK_SMTP_USER" ]; then
    smtp_server="$smtp_server,\"user\":\"$(json_escape "$KEYCLOAK_SMTP_USER")\""
  fi
  if [ -n "$KEYCLOAK_SMTP_PASSWORD" ]; then
    smtp_server="$smtp_server,\"password\":\"$(json_escape "$KEYCLOAK_SMTP_PASSWORD")\""
  fi

  smtp_server="$smtp_server}"

  "$KCADM" update "realms/$KEYCLOAK_REALM" -s "smtpServer=$smtp_server" >/dev/null
  log "Synchronized SMTP settings for realm '$KEYCLOAK_REALM'."
}

sync_blazor_client_settings() {
  local uuid
  local attributes

  uuid="$(client_uuid "$KEYCLOAK_BLAZOR_CLIENT_ID")"
  if [ -z "$uuid" ]; then
    fail "Keycloak client '$KEYCLOAK_BLAZOR_CLIENT_ID' was not found in realm '$KEYCLOAK_REALM'. Confirm realm import completed successfully."
  fi

  require_non_empty "KEYCLOAK_BLAZOR_REDIRECT_URIS" "$BLAZOR_REDIRECT_URIS"
  require_non_empty "KEYCLOAK_BLAZOR_WEB_ORIGINS" "$BLAZOR_WEB_ORIGINS"
  require_non_empty "KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS" "$BLAZOR_LOGOUT_REDIRECT_URIS"

  "$KCADM" update "clients/$uuid" -r "$KEYCLOAK_REALM" \
    -s "redirectUris=$BLAZOR_REDIRECT_URIS" \
    -s "webOrigins=$BLAZOR_WEB_ORIGINS" \
    -s 'attributes."pkce.code.challenge.method"="S256"' \
    -s "attributes.\"post.logout.redirect.uris\"=\"$BLAZOR_LOGOUT_REDIRECT_URIS\"" \
    -s 'attributes."backchannel.logout.session.required"="true"' \
    -s 'attributes."backchannel.logout.revoke.offline.tokens"="false"' \
    -s 'attributes."use.refresh.tokens"="true"' \
    -s 'attributes."oauth2.device.authorization.grant.enabled"="false"' \
    -s 'attributes."oidc.ciba.grant.enabled"="false"' >/dev/null

  log "Synchronized exact redirect URIs and web origins for client '$KEYCLOAK_BLAZOR_CLIENT_ID'."
}

main() {
  local blazor_secret

  require_non_empty "KEYCLOAK_ADMIN" "$KEYCLOAK_ADMIN"
  require_non_empty "KEYCLOAK_ADMIN_PASSWORD" "$KEYCLOAK_ADMIN_PASSWORD"

  if [ ! -x "$KCADM" ]; then
    fail "kcadm.sh was not found at '$KCADM'. Run this script inside the Keycloak image or set KCADM."
  fi

  log "Authenticating to Keycloak Admin API at ${KEYCLOAK_INTERNAL_URL} for realm '${KEYCLOAK_REALM}'."
  "$KCADM" config credentials \
    --server "$KEYCLOAK_INTERNAL_URL" \
    --realm master \
    --user "$KEYCLOAK_ADMIN" \
    --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null

  sync_realm_security_settings
  sync_realm_smtp_settings
  sync_blazor_client_settings

  blazor_secret="$(resolve_blazor_secret)"
  set_client_secret "$KEYCLOAK_BLAZOR_CLIENT_ID" "$blazor_secret"

  if [ -n "${KEYCLOAK_API_CLIENT_SECRET:-}" ]; then
    set_client_secret "$KEYCLOAK_API_CLIENT_ID" "$KEYCLOAK_API_CLIENT_SECRET"
  else
    log "KEYCLOAK_API_CLIENT_SECRET is unset; leaving optional API resource-server client secret unchanged."
  fi

  log "Keycloak client secret synchronization completed."
}

main "$@"
