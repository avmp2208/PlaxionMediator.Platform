#!/bin/bash
set -e

# Generate assets/config.json for the Dashboard (public-safe runtime config).
# Default to empty/disabled when variables are not set — no secrets ever written.
CONFIG_PATH="wwwroot/assets/config.json"
API_BASE_URL="${API_BASE_URL:-}"
AUTH_ENABLED="${Authentication__Enabled:-${AUTHENTICATION_ENABLED:-false}}"
AUTH_AUTHORITY="${Authentication__Authority:-${AUTHENTICATION_AUTHORITY:-}}"
OIDC_CLIENT_ID="${Dashboard__Oidc__ClientId:-${DASHBOARD_OIDC_CLIENT_ID:-}}"
OIDC_SCOPES="${Dashboard__Oidc__Scopes:-${DASHBOARD_OIDC_SCOPES:-openid profile email}}"
OIDC_REDIRECT_PATH="${Dashboard__Oidc__RedirectPath:-${DASHBOARD_OIDC_REDIRECT_PATH:-/auth/callback}}"
OIDC_POST_LOGOUT_PATH="${Dashboard__Oidc__PostLogoutRedirectPath:-${DASHBOARD_OIDC_POST_LOGOUT_REDIRECT_PATH:-/}}"

# Ensure wwwroot/assets exists
mkdir -p wwwroot/assets

# Normalize AUTH_ENABLED to JSON boolean
case "$(echo "$AUTH_ENABLED" | tr '[:upper:]' '[:lower:]')" in
  true|1|yes) AUTH_ENABLED_JSON="true" ;;
  *) AUTH_ENABLED_JSON="false" ;;
esac

# Build scopes JSON array from space- or comma-separated list
SCOPED_JSON="["
FIRST=1
# shellcheck disable=SC2086
for scope in $(echo "$OIDC_SCOPES" | tr ',' ' '); do
  scope="$(echo "$scope" | xargs)"
  [ -z "$scope" ] && continue
  if [ "$FIRST" -eq 1 ]; then
    FIRST=0
  else
    SCOPED_JSON+=", "
  fi
  SCOPED_JSON+="\"$scope\""
done
SCOPED_JSON+="]"
if [ "$SCOPED_JSON" = "[]" ]; then
  SCOPED_JSON='["openid", "profile", "email"]'
fi

# Escape values for JSON string embedding (minimal: backslash and quotes)
json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

API_BASE_URL_ESC="$(json_escape "$API_BASE_URL")"
AUTH_AUTHORITY_ESC="$(json_escape "$AUTH_AUTHORITY")"
OIDC_CLIENT_ID_ESC="$(json_escape "$OIDC_CLIENT_ID")"
OIDC_REDIRECT_PATH_ESC="$(json_escape "$OIDC_REDIRECT_PATH")"
OIDC_POST_LOGOUT_PATH_ESC="$(json_escape "$OIDC_POST_LOGOUT_PATH")"

if [ "$AUTH_ENABLED_JSON" = "true" ]; then
  AUTH_AUTHORITY_JSON="\"$AUTH_AUTHORITY_ESC\""
  CLIENT_ID_JSON="\"$OIDC_CLIENT_ID_ESC\""
else
  AUTH_AUTHORITY_JSON="null"
  CLIENT_ID_JSON="null"
fi

cat > "$CONFIG_PATH" <<EOF
{
  "apiBaseUrl": "$API_BASE_URL_ESC",
  "authentication": {
    "enabled": $AUTH_ENABLED_JSON,
    "authority": $AUTH_AUTHORITY_JSON,
    "clientId": $CLIENT_ID_JSON,
    "scopes": $SCOPED_JSON,
    "redirectPath": "$OIDC_REDIRECT_PATH_ESC",
    "postLogoutRedirectPath": "$OIDC_POST_LOGOUT_PATH_ESC"
  }
}
EOF

echo "Generated $CONFIG_PATH with apiBaseUrl: '$API_BASE_URL', authentication.enabled: $AUTH_ENABLED_JSON"

if [ "$ROLE" = "api" ]; then
    echo "Starting Platform.Api..."
    exec dotnet Platform.Api.dll "$@"
elif [ "$ROLE" = "ingestion" ]; then
    echo "Starting Platform.Ingestion..."
    exec dotnet Platform.Ingestion.dll "$@"
else
    echo "Error: ROLE environment variable must be set to 'api' or 'ingestion'."
    exit 1
fi
