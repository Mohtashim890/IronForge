#!/usr/bin/env bash
# Proves a backup can be restored: loads it into a throwaway PostgreSQL
# container on this server, prints row counts, then deletes the container.
# It never connects to the production database.
#
#   restore-test.sh                      newest backup in /opt/ironforge/backups
#   restore-test.sh <file.dump>          a specific backup
#
# Run it after setting up backups, then once a month (deploy/BACKUPS.md).

set -Eeuo pipefail

BACKUP_DIR="${BACKUP_DIR:-/opt/ironforge/backups}"
CONTAINER="ironforge-restore-test"
IMAGE="postgres:17-alpine"

BACKUP_FILE="${1:-}"

if [[ -z "$BACKUP_FILE" ]]; then
    BACKUP_FILE=$(ls -1t "$BACKUP_DIR"/ironforge-*.dump 2>/dev/null | head -n 1 || true)
fi

if [[ -z "$BACKUP_FILE" || ! -f "$BACKUP_FILE" ]]; then
    echo "ERROR: no backup file found (looked in $BACKUP_DIR)." >&2
    exit 1
fi

echo "Restoring $BACKUP_FILE into a temporary database..."

cleanup() {
    docker rm -f "$CONTAINER" > /dev/null 2>&1 || true
}
trap cleanup EXIT

cleanup

docker run -d --name "$CONTAINER" \
    -e POSTGRES_PASSWORD=restore-test \
    -v "$(cd "$(dirname "$BACKUP_FILE")" && pwd)":/restore:ro \
    "$IMAGE" > /dev/null

for _ in $(seq 1 30); do
    if docker exec "$CONTAINER" pg_isready -U postgres > /dev/null 2>&1; then
        break
    fi
    sleep 1
done

docker exec "$CONTAINER" pg_restore \
    --username=postgres \
    --dbname=postgres \
    --no-owner \
    --no-privileges \
    --clean \
    --if-exists \
    --exit-on-error \
    "/restore/$(basename "$BACKUP_FILE")"

echo
echo "Restored. Row counts:"

docker exec "$CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 -c "
    SELECT 'Tenants' AS table_name, count(*) FROM \"Tenants\"
    UNION ALL SELECT 'Users', count(*) FROM \"Users\"
    UNION ALL SELECT 'MemberProfiles', count(*) FROM \"MemberProfiles\"
    UNION ALL SELECT 'Invoices', count(*) FROM \"Invoices\"
    UNION ALL SELECT 'Payments', count(*) FROM \"Payments\"
    UNION ALL SELECT 'CheckIns', count(*) FROM \"CheckIns\";"

docker exec "$CONTAINER" psql -U postgres -d postgres -At -c \
    "SELECT 'Latest migration: ' || max(\"MigrationId\") FROM \"__EFMigrationsHistory\";"

echo
echo "Restore test passed. The temporary database is being removed."
