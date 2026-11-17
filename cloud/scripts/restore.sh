#!/usr/bin/env bash
# Restore a pg_dump backup into a target database (drop + recreate).
# Usage: ./scripts/restore.sh <backup-file> [target-db]
# Target defaults to POSTGRES_DB. The API is stopped during restore.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -f .env ]]; then
  set -a
  . ./.env
  set +a
fi

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <backup-file> [target-db]" >&2
  exit 1
fi

COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.prod.yml}"
DB_USER="${POSTGRES_USER:-edufocus}"
TARGET_DB="${2:-${POSTGRES_DB:-edufocus_cloud}}"
SOURCE="$1"
WORK_FILE="$SOURCE"

if [[ ! "$TARGET_DB" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "Invalid target database name: $TARGET_DB" >&2
  exit 1
fi
if [[ ! -f "$SOURCE" ]]; then
  echo "Backup file not found: $SOURCE" >&2
  exit 1
fi

cleanup() {
  if [[ "$WORK_FILE" != "$SOURCE" ]]; then
    rm -f "$WORK_FILE"
  fi
}
trap cleanup EXIT

if [[ "$SOURCE" == *.gpg ]]; then
  if [[ -z "${BACKUP_PASSPHRASE:-}" ]]; then
    echo "Set BACKUP_PASSPHRASE to decrypt $SOURCE" >&2
    exit 1
  fi
  WORK_FILE="$(mktemp)"
  gpg --batch --yes --decrypt --passphrase "$BACKUP_PASSPHRASE" --output "$WORK_FILE" "$SOURCE"
fi

run_psql() {
  docker compose -f "$COMPOSE_FILE" exec -T postgres \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d postgres "$@"
}

echo "Stopping API to prevent writes during restore..."
docker compose -f "$COMPOSE_FILE" stop api >/dev/null

run_psql -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TARGET_DB' AND pid <> pg_backend_pid();" >/dev/null
run_psql -c "DROP DATABASE IF EXISTS \"$TARGET_DB\";"
run_psql -c "CREATE DATABASE \"$TARGET_DB\";"

docker compose -f "$COMPOSE_FILE" exec -T postgres \
  pg_restore -U "$DB_USER" -d "$TARGET_DB" --no-owner --exit-on-error < "$WORK_FILE"

echo "Restored $SOURCE into database $TARGET_DB"
docker compose -f "$COMPOSE_FILE" start api >/dev/null
echo "API started again."
