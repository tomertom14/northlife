# Deployment and Operations

## Architecture

The production image builds Angular and serves its static files from ASP.NET Core on one origin. PostgreSQL is managed separately. Uploaded images live at `/var/data/images` on a persistent disk. One web instance is required because Render disks cannot attach to multiple instances.

## Render staging first

1. Fork `render.yaml` to a staging branch and give the service, database, and disk unique `-staging` names.
2. Create a Render Blueprint from that branch. The current plans are paid because persistent disks require a paid web service.
3. Set `GoogleMaps__ApiKey` to a browser key restricted to the staging and production hostnames. Set `GoogleMaps__MapId` to its map ID.
4. Keep PostgreSQL `ipAllowList: []`; the web service uses Render's private network values.
5. The pre-deploy command runs `dotnet NorthLife.Api.dll --migrate`. It must finish before the new web process starts.
6. Bootstrap the first administrator once from a Render Shell after setting the five `BootstrapAdmin__*` variables temporarily:

   ```sh
   dotnet NorthLife.Api.dll --bootstrap-admin
   ```

7. Verify `/health/live`, `/health/ready`, public feed/details/map, owner submission, admin approval, image restart persistence, and browser console/accessibility checks.
8. Create the production Blueprint only after staging passes. Use `checksPass` automatic deploys from `main`.

The Blueprint follows Render's current Docker, pre-deploy, health-check, private Postgres, and persistent-disk model: [Blueprint reference](https://render.com/docs/blueprint-spec), [Docker services](https://render.com/docs/docker), [persistent disks](https://render.com/docs/disks).

## Local production-image verification

```powershell
docker build -t northlife:local .
docker run --rm -p 10000:10000 `
  -e 'ConnectionStrings__Database=Host=host.docker.internal;Port=5432;Database=northlife;Username=northlife;Password=local-password' `
  -e 'Authentication__JwtKey=replace-with-at-least-32-random-bytes' `
  -v northlife-images:/var/data/images `
  northlife:local
```

Run migrations explicitly before starting a new image:

```powershell
docker run --rm `
  -e 'ConnectionStrings__Database=Host=host.docker.internal;Port=5432;Database=northlife;Username=northlife;Password=local-password' `
  -e 'Authentication__JwtKey=replace-with-at-least-32-random-bytes' `
  northlife:local --migrate
```

## Monitoring

- Render health check: `/health/ready`; liveness: `/health/live`.
- JSON logs contain method, path, status, elapsed milliseconds, application errors, and trace IDs. Query strings, request bodies, passwords, and authorization headers are not logged.
- Alert on failed deploys, repeated readiness failures, HTTP 5xx spikes, and sustained slow requests.
- Watch database storage/connections and disk capacity. The map endpoint caps responses at 200 markers.

## Backup and restore

- Enable Render Postgres retention appropriate to the environment. Before risky releases, create a manual database backup or run `pg_dump` against the external database URL from a trusted machine.
- Back up `/var/data/images` separately from PostgreSQL. From a Render Shell, archive `/var/data/images` and transfer the archive to protected external storage. Test restoring both the database and image archive together in staging.
- A database-only restore can leave missing image files; an image-only restore can leave unreferenced files. Always use backups from the same recovery point.

## Rollback

1. Stop automatic deploys if the failure is repeating.
2. Roll back to the last successful Render deploy/image.
3. Do not reverse a database migration unless a tested down migration and backup exist. Current migrations are additive; old application versions must remain compatible before promotion.
4. If data is damaged, restore PostgreSQL and images to staging first, verify both primary journeys, then restore production.
5. Confirm readiness, public browsing, owner submission, admin moderation, and image retrieval before re-enabling deploys.
