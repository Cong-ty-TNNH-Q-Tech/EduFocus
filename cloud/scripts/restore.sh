#!/usr/bin/env bash
# Restore a cold SQLite backup into the cloud data volume.
# Usage: ./scripts/restore.sh <backup-file>
# The API is stopped during restore and started again afterwards.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -f .env ]]; then
  set -a
  . ./.env
  set +a
fi

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <backup-file>" >&2
  exit 1
fi

COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"
SOURCE="$1"
WORK_FILE="$SOURCE"
API_STOPPED=false

cleanup() {
  local exit_code=$?
  if [[ "$WORK_FILE" != "$SOURCE" ]]; then
    rm -f "$WORK_FILE"
  fi
  if [[ "$API_STOPPED" == true ]]; then
    echo "Ensuring API service is started..." >&2
    if docker compose -f "$COMPOSE_FILE" start api >/dev/null 2>&1; then
      if [[ $exit_code -ne 0 ]]; then
        echo "Restore failed (exit $exit_code), but API service was restarted." >&2
      fi
    else
      echo "WARNING: Restore exited ($exit_code) and failed to restart API service." >&2
    fi
  fi
}
trap cleanup EXIT

if [[ ! -f "$SOURCE" ]]; then
  echo "Backup file not found: $SOURCE" >&2
  exit 1
fi

if [[ "$SOURCE" == *.gpg ]]; then
  if [[ -z "${BACKUP_PASSPHRASE:-}" ]]; then
    echo "Set BACKUP_PASSPHRASE to decrypt $SOURCE" >&2
    exit 1
  fi
  WORK_FILE="$(mktemp)"
  gpg --batch --yes --pinentry-mode loopback --decrypt \
    --passphrase-fd 3 --output "$WORK_FILE" "$SOURCE" 3<<<"$BACKUP_PASSPHRASE"
fi

if ! tar -tzf "$WORK_FILE" >/dev/null 2>&1; then
  echo "Backup is not a valid tar.gz archive: $SOURCE" >&2
  exit 1
fi
if ! tar -tzf "$WORK_FILE" | grep -Eq '^\./edufocus\.db$|^edufocus\.db$'; then
  echo "Backup does not contain edufocus.db: $SOURCE" >&2
  exit 1
fi

echo "Stopping API to prevent writes during restore..."
docker compose -f "$COMPOSE_FILE" stop api >/dev/null
API_STOPPED=true

docker compose -f "$COMPOSE_FILE" run --rm --no-deps -T --entrypoint sh api -c '
  set -e
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  for file in edufocus.db edufocus.db-wal edufocus.db-shm; do
    if [ -f "/data/$file" ]; then
      mv "/data/$file" "/data/$file.pre-restore-$stamp"
    fi
  done
  tar -xzf - -C /data
' < "$WORK_FILE"

echo "Restored $SOURCE into the SQLite volume."
docker compose -f "$COMPOSE_FILE" start api >/dev/null
API_STOPPED=false
echo "API started again."
