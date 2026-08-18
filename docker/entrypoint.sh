#!/bin/bash
set -e

# Generate assets/config.json for the Dashboard
# Default to empty if API_BASE_URL is not set
CONFIG_PATH="wwwroot/assets/config.json"
API_BASE_URL="${API_BASE_URL:-}"

# Ensure wwwroot/assets exists
mkdir -p wwwroot/assets

echo "{\"apiBaseUrl\": \"$API_BASE_URL\"}" > "$CONFIG_PATH"
echo "Generated $CONFIG_PATH with apiBaseUrl: '$API_BASE_URL'"

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
