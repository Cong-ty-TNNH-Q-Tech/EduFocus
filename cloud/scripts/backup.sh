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

if [[ -n "${BACKUP_PASSPHRASE:-}" ]] && ! command -v gpg >/dev/null 2>&1; then
  echo "gpg is required to encrypt backups but was not found." >&2
  exit 1
fi

tmp_dump="$(mktemp "$BACKUP_DIR/.edufocus-$STAMP.dump.XXXXXX")"
tmp_gpg=""
cleanup() {
  rm -f "${tmp_dump:-}" "${tmp_gpg:-}"
}
trap cleanup EXIT

docker compose -f "$COMPOSE_FILE" exec -T postgres \
  pg_dump -U "$DB_USER" -d "$DB_NAME" --format=custom --no-owner > "$tmp_dump"
if [[ ! -s "$tmp_dump" ]]; then
  echo "pg_dump produced an empty backup file." >&2
  exit 1
fi
chmod 600 "$tmp_dump"

dump_file="$BACKUP_DIR/edufocus-$STAMP.dump"
if [[ -n "${BACKUP_PASSPHRASE:-}" ]]; then
  tmp_gpg="$(mktemp "$BACKUP_DIR/.edufocus-$STAMP.dump.gpg.XXXXXX")"
  gpg --batch --yes --pinentry-mode loopback --symmetric --cipher-algo AES256 \
    --passphrase-fd 3 --output "$tmp_gpg" "$tmp_dump" 3<<<"$BACKUP_PASSPHRASE"
  chmod 600 "$tmp_gpg"
  dump_file="$dump_file.gpg"
  mv "$tmp_gpg" "$dump_file"
  tmp_gpg=""
  rm -f "$tmp_dump"
  tmp_dump=""
else
  mv "$tmp_dump" "$dump_file"
  tmp_dump=""
fi

mapfile -t backups < <(ls -1t "$BACKUP_DIR"/edufocus-*.dump* 2>/dev/null || true)
if ((${#backups[@]} > BACKUP_KEEP)); then
  printf '%s\n' "${backups[@]:BACKUP_KEEP}" | xargs -r rm -f
fi

echo "Backup written: $dump_file"
