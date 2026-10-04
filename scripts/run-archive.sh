#!/usr/bin/env bash

set -u

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
LOG_DIR="$PROJECT_DIR/logs"
LOG_FILE="$LOG_DIR/archive-cron.log"
DLL_PATH="$PROJECT_DIR/publish/DonationArchiving.dll"
ENV_FILE="${ARCHIVE_ENV_FILE:-$PROJECT_DIR/.env}"

mkdir -p "$LOG_DIR"
exec >>"$LOG_FILE" 2>&1

echo "[$(date --iso-8601=seconds)] Archive Cron execution started."

if [[ -f "$ENV_FILE" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
fi

if [[ ! -f "$DLL_PATH" ]]; then
    echo "[$(date --iso-8601=seconds)] ERROR: Published application not found: $DLL_PATH"
    echo "Run scripts/install-cron.sh to publish and install the Cron entry."
    exit 1
fi

if [[ -n "${DOTNET_BIN:-}" ]]; then
    DOTNET_COMMAND="$DOTNET_BIN"
elif command -v dotnet >/dev/null 2>&1; then
    DOTNET_COMMAND="$(command -v dotnet)"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
    DOTNET_COMMAND="$HOME/.dotnet/dotnet"
else
    echo "[$(date --iso-8601=seconds)] ERROR: dotnet was not found."
    exit 1
fi

cd "$PROJECT_DIR" || exit 1

"$DOTNET_COMMAND" "$DLL_PATH" archive
exit_code=$?

echo "[$(date --iso-8601=seconds)] Archive Cron execution finished with exit code $exit_code."
exit "$exit_code"
