#!/usr/bin/env bash
# Copyright (c) Rivoli AI 2026. All rights reserved.
#
# run-dev.sh — start the API in dotnet mode.
#
# Sources .env.dotnet so the .NET host picks up dep URLs via the
# `__`-separated environment-variable mapping. Generates .env.dotnet first
# if it is missing or stale.
#
# Usage:
#   ./scripts/run-dev.sh                # run the API
#   ./scripts/run-dev.sh -- --no-launch-profile  # forward args to dotnet run

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env.dotnet"

if [[ ! -f "$ENV_FILE" ]]; then
  "$REPO_ROOT/scripts/sync-dep-ports.sh"
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

# Translate canonical names to .NET's `__`-separated config keys.
export AndyAuth__Authority="$ANDY_AUTH_AUTHORITY"
export AndyAuth__Audience="$ANDY_AUTH_AUDIENCE"
export Rbac__ApiBaseUrl="$ANDY_RBAC_API_BASE_URL"
export Rbac__ApplicationCode="$ANDY_RBAC_APPLICATION_CODE"
export AndySettings__ApiBaseUrl="$ANDY_SETTINGS_API_BASE_URL"
export AndySettings__ApplicationCode="$ANDY_SETTINGS_APPLICATION_CODE"

API_PROJECT="$(find "$REPO_ROOT/src" -maxdepth 2 -name "*.Api.csproj" -print -quit)"
[[ -n "$API_PROJECT" ]] || { echo "error: could not locate *.Api.csproj under $REPO_ROOT/src" >&2; exit 1; }

exec dotnet run --project "$API_PROJECT" "$@"
