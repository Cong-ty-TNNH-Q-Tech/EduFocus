#!/usr/bin/env bash
# Daily logical backup of the cloud PostgreSQL database.
# Usage: ./scripts/backup.sh   (reads .env next to docker-compose.prod.yml)
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -f .env ]]; then
  set -a
  . ./.env
  set +a
fi

COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.prod.yml}"
BACKUP_DIR="${BACKUP_DIR:-backups}"
BACKUP_KEEP="${BACKUP_KEEP:-7}"
DB_NAME="${POSTGRES_DB:-edufocus_cloud}"
DB_USER="${POSTGRES_USER:-edufocus}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

umask 077
mkdir -p "$BACKUP_DIR"

dump_file="$BACKUP_DIR/edufocus-$STAMP.dump"
docker compose -f "$COMPOSE_FILE" exec -T postgres \
  pg_dump -U "$DB_USER" -d "$DB_NAME" --format=custom --no-owner > "$dump_file"
chmod 600 "$dump_file"

if [[ -n "${BACKUP_PASSPHRASE:-}" ]]; then
  if ! command -v gpg >/dev/null 2>&1; then
    echo "gpg is required to encrypt backups but was not found." >&2
    exit 1
  fi
  gpg --batch --yes --symmetric --cipher-algo AES256 \
    --passphrase "$BACKUP_PASSPHRASE" --output "$dump_file.gpg" "$dump_file"
  rm -f "$dump_file"
  dump_file="$dump_file.gpg"
fi

mapfile -t backups < <(ls -1t "$BACKUP_DIR"/edufocus-*.dump* 2>/dev/null || true)
if ((${#backups[@]} > BACKUP_KEEP)); then
  printf '%s\n' "${backups[@]:BACKUP_KEEP}" | xargs -r rm -f
fi

echo "Backup written: $dump_file"
