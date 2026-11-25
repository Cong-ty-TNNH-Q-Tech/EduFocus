#!/usr/bin/env bash
# Daily cold backup of the cloud SQLite database volume.
# Usage: ./scripts/backup.sh   (reads .env next to docker-compose.yml)
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -f .env ]]; then
  set -a
  . ./.env
  set +a
fi

COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"
BACKUP_DIR="${BACKUP_DIR:-backups}"
BACKUP_KEEP="${BACKUP_KEEP:-7}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

umask 077
mkdir -p "$BACKUP_DIR"

if [[ -n "${BACKUP_PASSPHRASE:-}" ]] && ! command -v gpg >/dev/null 2>&1; then
  echo "gpg is required to encrypt backups but was not found." >&2
  exit 1
fi

tmp_archive="$(mktemp "$BACKUP_DIR/.edufocus-$STAMP.tar.gz.XXXXXX")"
tmp_gpg=""
api_was_running=false
cleanup() {
  if [[ -n "${tmp_archive:-}" ]]; then
    rm -f "$tmp_archive"
  fi
  if [[ -n "${tmp_gpg:-}" ]]; then
    rm -f "$tmp_gpg"
  fi
  if [[ "$api_was_running" == true ]]; then
    echo "Ensuring API service is started..." >&2
    docker compose -f "$COMPOSE_FILE" start api >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

if docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -qx api; then
  api_was_running=true
  echo "Stopping API for a consistent SQLite snapshot..."
  docker compose -f "$COMPOSE_FILE" stop api >/dev/null
fi

docker compose -f "$COMPOSE_FILE" run --rm --no-deps -T \
  --entrypoint sh api -c 'tar -czf - -C /data .' > "$tmp_archive"
if [[ ! -s "$tmp_archive" ]]; then
  echo "SQLite backup produced an empty archive." >&2
  exit 1
fi
chmod 600 "$tmp_archive"

if [[ "$api_was_running" == true ]]; then
  docker compose -f "$COMPOSE_FILE" start api >/dev/null
  api_was_running=false
fi

backup_file="$BACKUP_DIR/edufocus-$STAMP.tar.gz"
if [[ -n "${BACKUP_PASSPHRASE:-}" ]]; then
  tmp_gpg="$(mktemp "$BACKUP_DIR/.edufocus-$STAMP.tar.gz.gpg.XXXXXX")"
  gpg --batch --yes --pinentry-mode loopback --symmetric --cipher-algo AES256 \
    --passphrase-fd 3 --output "$tmp_gpg" "$tmp_archive" 3<<<"$BACKUP_PASSPHRASE"
  chmod 600 "$tmp_gpg"
  backup_file="$backup_file.gpg"
  mv "$tmp_gpg" "$backup_file"
  tmp_gpg=""
  rm -f "$tmp_archive"
  tmp_archive=""
else
  mv "$tmp_archive" "$backup_file"
  tmp_archive=""
fi

mapfile -t backups < <(ls -1t "$BACKUP_DIR"/edufocus-*.tar.gz* 2>/dev/null || true)
if ((${#backups[@]} > BACKUP_KEEP)); then
  printf '%s\n' "${backups[@]:BACKUP_KEEP}" | xargs -r rm -f
fi

echo "Backup written: $backup_file"
