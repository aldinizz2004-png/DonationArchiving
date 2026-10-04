#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
RUNNER="$SCRIPT_DIR/run-archive.sh"
MARKER="# DonationArchiving automatic archive"
SCHEDULE="*/5 * * * *"

chmod +x "$RUNNER" "$0"
mkdir -p "$PROJECT_DIR/logs"

echo "Publishing DonationArchiving in Release mode..."
dotnet publish "$PROJECT_DIR/DonationArchiving.csproj" \
    --configuration Release \
    --output "$PROJECT_DIR/publish"

current_crontab="$(crontab -l 2>/dev/null || true)"
filtered_crontab="$(printf '%s\n' "$current_crontab" | grep -Fv "$MARKER" || true)"
cron_entry="$SCHEDULE \"$RUNNER\" $MARKER"

{
    printf '%s\n' "$filtered_crontab"
    printf '%s\n' "$cron_entry"
} | sed '/^[[:space:]]*$/d' | crontab -

echo "Installed Cron entry:"
crontab -l | grep -F "$MARKER"
