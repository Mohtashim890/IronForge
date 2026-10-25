# Database backups

The database (members, invoices, payments, check-ins) is the gyms' business record. It is backed up three ways:

| What | Where | When | Kept |
|---|---|---|---|
| `ironforge-backup` container (`pg_dump`, custom format) | VPS: `/opt/ironforge/backups` | daily at `BACKUP_HOUR_UTC` (default 21:00 UTC = 02:00 PKT / 05:00 MYT) | `BACKUP_KEEP_DAYS` (default 14) |
| Pre-migration backup (`deploy.sh`) | same folder | before every deployment's migrations | same |
| Supabase's own backups | Supabase | daily on Pro plan (Point-in-Time Recovery as an add-on) | per plan |

The VPS copy is on a different provider from Supabase, so losing either one alone loses no data.
**The Supabase Free plan has no backups**: use Pro (or higher) once a real gym is on the platform.

## Set up (once, on the VPS)

1. Copy the files the compose file mounts:
   ```bash
   sudo mkdir -p /opt/ironforge/backups && sudo chmod 700 /opt/ironforge/backups
   # from the repository:
   scp deploy/scripts/backup.sh deploy/scripts/restore-test.sh deploy/scripts/deploy.sh <vps>:/opt/ironforge/scripts/
   scp deploy/compose.production.yml <vps>:/opt/ironforge/compose/
   ```
2. Add to `/opt/ironforge/env/production.env` (see `deploy/production.env.example`):
   ```
   BACKUP_DATABASE_URL=postgresql://postgres.<project>:<password>@<host>:5432/postgres?sslmode=require
   BACKUP_KEEP_DAYS=14
   BACKUP_PING_URL=https://hc-ping.com/<uuid>
   ```
   Use Supabase's **direct connection or session pooler (port 5432)**. The transaction pooler (6543) does not work with `pg_dump`.
3. Create a free check at [healthchecks.io](https://healthchecks.io) (period 1 day, grace 2 hours) and put its ping URL in `BACKUP_PING_URL`. You get an email when a nightly backup does not arrive.
4. Start it and take the first backup:
   ```bash
   cd /opt/ironforge/compose
   docker compose --env-file ../env/production.env -f compose.production.yml up -d ironforge-backup
   docker compose --env-file ../env/production.env -f compose.production.yml run --rm ironforge-backup once
   ls -lh /opt/ironforge/backups
   ```
5. **Test the restore** (next section). A backup that was never restored is not known to work.

## Test a restore (after set-up, then monthly)

```bash
sudo bash /opt/ironforge/scripts/restore-test.sh          # newest backup
sudo bash /opt/ironforge/scripts/restore-test.sh /opt/ironforge/backups/ironforge-20261020-210000.dump
```

It restores into a throwaway PostgreSQL container, prints row counts (gyms, users, members, invoices, payments, check-ins) and the latest migration, then deletes the container. It never touches production. Compare the counts with the Platform console overview.

## Off-server copy (recommended)

Backups on the VPS are lost if the VPS is. Copy them somewhere else daily, for example with `rclone` to Backblaze B2, Cloudflare R2 or Google Drive (cron on the VPS):

```
30 22 * * * rclone copy /opt/ironforge/backups remote:ironforge-backups --max-age 48h
```

The dumps contain members' personal data: keep the remote private (and encrypted, e.g. an `rclone crypt` remote).

## Restore production (an emergency)

1. Stop the apps so nothing writes meanwhile:
   `docker compose --env-file ../env/production.env -f compose.production.yml stop ironforge-api ironforge-mcp ironforge-web ironforge-platform`
2. Prefer restoring into a **new** Supabase project (or a new database), check it with the row counts, then point `ConnectionStrings__DefaultConnection` at it.
3. Restore (from the VPS, with the target's direct connection URL):
   ```bash
   docker run --rm -v /opt/ironforge/backups:/backups:ro postgres:17-alpine \
     pg_restore --no-owner --no-privileges --clean --if-exists --exit-on-error \
     --dbname="postgresql://...:5432/postgres?sslmode=require" /backups/ironforge-<stamp>.dump
   ```
4. Start the apps again (`up -d`) and check `/health/ready`, the Platform console and a gym dashboard.

Members' passes keep working offline throughout. Payments taken by card during the gap are re-sent by the payment provider's webhooks, which are processed once each.
