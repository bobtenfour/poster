# Poster Print Request — Docker evaluation deployment

**DEMO / EVALUATION ONLY.** Deploys Poster Print Request beside DentalInventory on the same DigitalOcean Droplet. TLS stays on the existing Caddy ingress. The site block matches `dinventory.blueignix.com`.

| Aspect | Value |
| --- | --- |
| Droplet path | `/opt/posters` |
| Compose project | `posterprintrequest-demo` |
| Public origin | `https://poster.blueignix.com` |
| Web bind | `127.0.0.1:8083` |
| SQL Server | Existing `dentalinventory-demo-sql` on network `dentalinventory-demo_default` (alias `sqlserver:1433`) |
| Database | **`POSTER` only** |
| Shared storage | Named volume `posterprintrequest-demo-storage` mounted at `/var/poster-print-request` |
| Accounts | `usera` / `prueba1` role User; `usero` / `prueba1` role Operator |

This compose file does not create a SQL Server container and does not change DentalInventory or KeyInventory databases, volumes, or site blocks.

The web application does not call `Database.Migrate()` at startup. The `migrate` service is the only migration owner.

## Droplet commands

Run these on the existing Droplet `159.203.182.9`. Do not run them from the development machine as part of preparing the repository.

Copy the repository to the Droplet from the development machine, excluding build output:

```cmd
tar -czf %TEMP%\posters-demo.tgz --exclude=bin --exclude=obj --exclude=.vs --exclude=TestResults --exclude=.env.demo -C C:\projects posters
scp %TEMP%\posters-demo.tgz root@159.203.182.9:/tmp/posters-demo.tgz
ssh root@159.203.182.9 "mkdir -p /opt/posters && tar -xzf /tmp/posters-demo.tgz -C /opt && rm -f /tmp/posters-demo.tgz"
```

On the Droplet, set the existing SQL password and start Poster only:

```bash
ssh root@159.203.182.9
cd /opt/posters
test -f .env.demo || cp .env.demo.template .env.demo
chmod 600 .env.demo
# Set MSSQL_SA_PASSWORD in .env.demo to the password already used by dentalinventory-demo-sql.
docker inspect dentalinventory-demo-sql >/dev/null
docker network inspect dentalinventory-demo_default >/dev/null
docker compose -f docker-compose.demo.yml --env-file .env.demo build
docker compose -f docker-compose.demo.yml --env-file .env.demo up -d
docker compose -f docker-compose.demo.yml --env-file .env.demo ps
docker compose -f docker-compose.demo.yml --env-file .env.demo logs migrate
curl -fsS http://127.0.0.1:8083/health
bash docker/install-poster-caddy-site.sh
curl -fsS https://poster.blueignix.com/health
```

`install-poster-caddy-site.sh` appends `docker/caddy/poster.blueignix.caddy` to `/etc/caddy/Caddyfile` only when `poster.blueignix.com` is absent. It leaves the file unchanged when validation fails, and it does not rewrite `dinventory.blueignix.com` or `kinventory.blueignix.com`.

Sign in at `https://poster.blueignix.com` as `usera` or `usero` with password `prueba1`.
