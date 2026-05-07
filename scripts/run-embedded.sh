#!/usr/bin/env bash
# Copyright (c) Rivoli AI 2026. All rights reserved.
#
# run-embedded.sh — start the API in embedded mode (Conductor-style).
#
# Sources .env.embedded so the .NET host reaches deps via the Conductor unified
# proxy on http://localhost:9100. Forces the SQLite provider so the service can
# run without a separate Postgres. Useful for local Conductor parity testing
# without launching Conductor itself.
#
# Usage:
#   ./scripts/run-embedded.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env.embedded"

if [[ ! -f "$ENV_FILE" ]]; then
  "$REPO_ROOT/scripts/sync-dep-ports.sh"
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

export AndyAuth__Authority="$ANDY_AUTH_AUTHORITY"
export AndyAuth__Audience="$ANDY_AUTH_AUDIENCE"
export Rbac__ApiBaseUrl="$ANDY_RBAC_API_BASE_URL"
export Rbac__ApplicationCode="$ANDY_RBAC_APPLICATION_CODE"
export AndySettings__ApiBaseUrl="$ANDY_SETTINGS_API_BASE_URL"
export AndySettings__ApplicationCode="$ANDY_SETTINGS_APPLICATION_CODE"
export Database__Provider="Sqlite"

API_PROJECT="$(find "$REPO_ROOT/src" -maxdepth 2 -name "*.Api.csproj" -print -quit)"
[[ -n "$API_PROJECT" ]] || { echo "error: could not locate *.Api.csproj under $REPO_ROOT/src" >&2; exit 1; }

exec dotnet run --project "$API_PROJECT" "$@"
