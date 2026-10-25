#!/bin/sh
# IronForge database backup (runs in the ironforge-backup container, postgres:17-alpine).
#
#   backup.sh once   one backup now (deploy.sh runs this before every migration)
#   backup.sh loop   one backup a day at BACKUP_HOUR_UTC, forever (the container's command)
#
# Environment (deploy/production.env):
#   BACKUP_DATABASE_URL   postgresql://user:password@host:5432/postgres?sslmode=require
#                         The direct connection, not the transaction pooler (port 6543).
#   BACKUP_KEEP_DAYS      delete backups older than this (default 14)
#   BACKUP_HOUR_UTC       hour of the daily backup (default 21 UTC = 02:00 in Pakistan, 05:00 in Malaysia)
#   BACKUP_PING_URL       optional; requested after each successful backup (healthchecks.io or similar)
#
# Only the "public" schema is dumped: it holds every IronForge table and the
# EF migrations history. Supabase's own schemas (auth, storage, ...) are not needed.

set -eu

BACKUP_DIR="${BACKUP_DIR:-/backups}"
KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"
HOUR_UTC="${BACKUP_HOUR_UTC:-21}"

log() {
    echo "$(date -u '+%Y-%m-%d %H:%M:%S') ironforge-backup: $*"
}

backup_once() {
    if [ -z "${BACKUP_DATABASE_URL:-}" ]; then
        log "ERROR: BACKUP_DATABASE_URL is not set."
        return 1
    fi

    mkdir -p "$BACKUP_DIR"
    chmod 700 "$BACKUP_DIR"

    stamp="$(date -u '+%Y%m%d-%H%M%S')"
    target="$BACKUP_DIR/ironforge-$stamp.dump"
    partial="$target.partial"

    log "Backing up to $target"

    # Custom format: compressed, and pg_restore can restore single tables from it.
    if ! pg_dump \
        --format=custom \
        --compress=9 \
        --schema=public \
        --no-owner \
        --no-privileges \
        --file="$partial" \
        "$BACKUP_DATABASE_URL"; then
        rm -f "$partial"
        log "ERROR: pg_dump failed."
        return 1
    fi

    # A readable table of contents proves the file is a complete archive.
    if ! pg_restore --list "$partial" > /dev/null; then
        rm -f "$partial"
        log "ERROR: the backup file is not readable."
        return 1
    fi

    mv "$partial" "$target"
    chmod 600 "$target"

    log "Backup complete ($(du -h "$target" | cut -f1))."

    find "$BACKUP_DIR" -name 'ironforge-*.dump' -type f -mtime +"$KEEP_DAYS" -print -delete |
        while read -r removed; do log "Removed old backup $removed"; done

    date -u '+%Y-%m-%dT%H:%M:%SZ' > "$BACKUP_DIR/last-success"

    if [ -n "${BACKUP_PING_URL:-}" ]; then
        wget -q -O /dev/null -T 10 "$BACKUP_PING_URL" || log "WARNING: could not ping BACKUP_PING_URL."
    fi
}

seconds_until_next_run() {
    hour=$(expr "$HOUR_UTC" + 0)
    since_midnight=$(( $(date -u +%s) % 86400 ))
    wait_seconds=$(( (hour * 3600 - since_midnight + 86400) % 86400 ))

    if [ "$wait_seconds" -eq 0 ]; then
        wait_seconds=86400
    fi

    echo "$wait_seconds"
}

case "${1:-once}" in
    once)
        backup_once
        ;;
    loop)
        log "Daily backups at $HOUR_UTC:00 UTC, kept $KEEP_DAYS days."
        while true; do
            wait_seconds=$(seconds_until_next_run)
            log "Next backup in $((wait_seconds / 60)) minutes."
            sleep "$wait_seconds"
            # A failed night is logged (and the missing ping alerts); the loop keeps going.
            backup_once || log "ERROR: backup failed; retrying tomorrow."
        done
        ;;
    *)
        echo "Usage: $0 once|loop" >&2
        exit 1
        ;;
esac
