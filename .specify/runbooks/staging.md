# WMS standalone staging

This runbook covers independent `dev` and `test` deployments on `vm-xms-dev`.
They use separate databases, ports, and application containers, and share one
Caddy instance with a persistent internal CA. 1C currently uses HTTP WebApi;
WebApp and standalone Mobile use HTTPS through Caddy.

## Topology

| Environment | Service | HTTP | HTTPS |
| --- | --- | --- | --- |
| dev | WebApi | `8206` | `8216` |
| dev | WebApp | `8207` | `8217` |
| test | WebApi | `8306` | `8316` |
| test | WebApp | `8307` | `8317` |

The Mobile Release build uses `https://vm-xms-dev:8316/`.
Warehouse Wi-Fi must supply the `dobroga.local` DNS search domain so Android
resolves the single-label host `vm-xms-dev`. Without it, Mobile cannot reach
Caddy even when the same DNS server addresses are configured.

## First deployment

Run `dotnet`, `adb`, and `publish.ps1` from the repository root on the development
workstation. The publish script also needs Docker there to tag and push images.
Run Compose commands, export the Caddy certificate, and manage environment files
from `scripts/` on the Docker host.

### 1. Environment and network

On the Docker host, create the untracked environment files in `scripts/`:

```sh
cp dev.env.example dev.env
cp test.env.example test.env
```

Set a published `WMS_TAG`, distinct database connection strings, administrator
credentials, and 1C credentials. `dev.env` and `test.env` are secrets and must
not be committed. Compose explicitly maps their values to ASP.NET configuration.

Both Compose projects require the external network:

```powershell
docker network inspect xms-network
docker network create xms-network
```

Run the create command only when inspect reports that the network is absent.

`docker-compose.yml` mounts `openssl-legacy.cnf` because the current SQL Server
requires legacy TLS settings. Keep the file beside the Compose definition until
that server is upgraded; removing it earlier can break database connectivity.

### 2. Databases

The initial migrations are `CreateIdentitySchema` and
`CreateInitialWmsSchema`; the update command applies them and all later
migrations. Run it separately for empty dev and test databases:

```powershell
dotnet ef database update --project Wms.Infrastructure/Wms.Infrastructure.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj --connection "<dev connection string>"
dotnet ef database update --project Wms.Infrastructure/Wms.Infrastructure.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj --connection "<test connection string>"
```

After baseline deployment, migrations are append-only and applied normally.

For a development database that may be discarded, the SQL scripts
`scripts/clear-wms-operational-data.sql` and
`scripts/clear-database-except-identity.sql` remove operational data; the second
also removes catalogs and topology. Stop WebApp, WebApi, and integration workers
first. Both preserve Identity, EF migration history, and the issued-LPN sequence,
but invalidate previously printed pallet labels. Review existing data and
migration compatibility separately before changing a non-disposable database.

### Cutover rehearsal database

Use a separate disposable database for each warehouse-cutover rehearsal. The
opening import allows one topology stage and one stock snapshot per warehouse;
a changed workbook or a new request cannot replace either stage. Keep the XLSX,
snapshot time, source SHA-256 and saved import protocol with the rehearsal
results. Inspect the protocol on the Administrator «Переход со старой WMS» page
before accepting the rehearsal.
Before catalog synchronization, check the effective `ONEC_BASEADDRESS` for
that WebApp against the 1C UT database corresponding to the export. The
repository's development defaults use `dobroga2025_dev2`; a production export
may contain SKU GUIDs absent from that copy. A rehearsal that reads a different
1C database needs its own isolated environment so its catalog and other 1C
operations cannot affect shared dev/test work.

For another rehearsal, stop that environment's WebApp, WebApi and integration
workers. Restore a clean disposable database, or run
`scripts/clear-database-except-identity.sql` against the intended disposable
database, then resynchronize the 1C SKU and unit catalogs before loading stock.
The full-clear script also removes opening-import records, audit, movements,
balances, topology and catalogs, while preserving Identity and migration history.
Recreate the target warehouse and its access settings before the next import.
`scripts/clear-wms-operational-data.sql` keeps topology, the opening-import
record, its audit and command receipts, and catalog-change history. It removes
balances and movements, so the saved import summary no longer describes current
stock and the warehouse cannot use the script for another cutover rehearsal.
Database cleanup is an environment operation, never a cutover-page action.

At the final transition, complete or separately resolve open work in the old
WMS, stop its warehouse operations, and export the complete workbook. Preview
and confirm topology in the new WMS; synchronize SKU and unit catalogs; preview
and confirm stock from the same unchanged workbook with its recorded Moscow-time
snapshot. Inspect the saved protocol and balances, then resume warehouse work
only in the new WMS. The export does not migrate unfinished orders, LPNs or
transfers. The detailed checks are in the
[cutover validation guide](../../specs/010-topology-opening-balances/quickstart.md).

### 3. Publish and start applications

Publish both container images with one tag. The script generates a timestamp tag
unless `-Tag` is supplied:

```powershell
.\scripts\publish.ps1
```

Set the emitted tag in the target environment file, then on the Docker host run:

```powershell
docker compose --env-file dev.env -f docker-compose.yml up -d --force-recreate
docker compose --env-file test.env -f docker-compose.yml up -d --force-recreate
docker compose --env-file dev.env -f docker-compose.yml ps
docker compose --env-file test.env -f docker-compose.yml ps
```

Expected containers are `wms-webapi-dev`, `wms-webapp-dev`,
`wms-webapi-test`, and `wms-webapp-test`.

### 4. Caddy and client trust

Start Caddy and inspect startup logs:

```powershell
docker compose -f docker-compose-caddy.yml up -d
docker compose -f docker-compose-caddy.yml logs caddy
```

Do not use `docker compose down -v`: deleting `wms-caddy-data` replaces the CA
and invalidates client trust. A `502` normally means an application container
is absent or not attached to `xms-network`.

On the Docker host, export the public root certificate after first deployment
or an intentional CA replacement:

```sh
docker cp wms-caddy:/data/caddy/pki/authorities/local/root.crt ./wms-caddy-root.crt
```

Copy only the public certificate to `scripts/wms-caddy-root.crt` on the
development workstation. Never distribute the CA private key. Install the
root from an elevated terminal:

```powershell
certutil -addstore -f Root .\scripts\wms-caddy-root.crt
```

On Android, copy and install it as a **CA certificate**:

```powershell
adb devices
adb push .\scripts\wms-caddy-root.crt /sdcard/Download/wms-caddy-root.crt
```

The Mobile network policy trusts user-installed roots only for `vm-xms-dev`.
Caddy's `default_sni vm-xms-dev` is required for the verified Urovo Android 9
client, which does not send SNI for this single-label host.

### 5. Mobile

Before installation, the device browser must reach the following URL without a
certificate warning and receive `401 Unauthorized`:

```text
https://vm-xms-dev:8316/api/mobile/v1/me
```

Build and install the standalone ARM64 APK:

```powershell
dotnet publish .\Wms.Mobile\Wms.Mobile.csproj -f net10.0-android -c Release -r android-arm64
adb install -r .\Wms.Mobile\bin\Release\net10.0-android\android-arm64\publish\ru.igmo.wms.mobile-Signed.apk
```

`adb install -r` preserves application data. Use the following only when a
clean local state is explicitly required; it clears the saved session and app
data, not the installed CA:

```powershell
adb shell pm clear ru.igmo.wms.mobile
```

## Verification

1. Open `https://vm-xms-dev:8217` and `https://vm-xms-dev:8317` without
   certificate warnings.
2. Disconnect USB, sign in to Mobile over warehouse Wi-Fi, scan a known SKU in
   diagnostics, and open an operational queue or document.
3. Confirm that the path is Mobile → Wi-Fi/DNS → Caddy → test WebApi → test
   database.

## Warehouse release rehearsal

Before a warehouse release, record one rehearsal with the application tag,
database migration version, Mobile build, warehouse, actors and results.

1. Restore a backup into an isolated test database, apply the release migrations
   and confirm that the selected application version works with that schema.
2. Check the enabled Web and Mobile workflows on representative dated stock.
   Include receiving, putaway, picking with replenishment and closing with
   shipment. Include replacement transfer and count workflows when they enter
   the release scope. Verify the explicit Operator, Manager and Administrator
   permissions for the delivered operations.
3. Interrupt a warehouse command and repeat it after reopening its state. Check
   the single warehouse effect, movement history, balances and command receipt.
   Simulate PATCH/POST failures in the test 1C database and verify the retained
   warehouse fact and repeat of only the unconfirmed documents through the same
   closing action.
4. Verify the effective network access to incoming 1C endpoints and the
   environment's logging settings. Keep endpoints without authentication inside
   the trusted network. Review SQL/TLS compatibility and current dependency
   advisories through [issue 41](https://github.com/igmo71/Wms/issues/41).
5. Save the rehearsal results and link unresolved problems to their Issues.

## Routine update

1. Publish and record one tag.
2. Set `WMS_TAG` in the target env file.
3. Apply new migrations to that environment.
4. Recreate its WebApi and WebApp containers.
5. Rebuild and install Mobile only when Mobile changed.
6. Run smoke checks for the changed workflows.

Caddy and its CA remain untouched during an ordinary update.

## Connectivity checklist

When Mobile cannot reach WMS, check in this order:

1. On the Mobile start page, run **Проверить подключение**. The report appears
   on the screen and can be photographed or copied to the device clipboard.
   It records the last sign-in/session transport error, the device's active
   network interface, IP addresses, DNS servers and search domain, DNS lookup
   of the configured host, and an unauthenticated HTTPS request to `/me`.
   `401` means the request reached WebApi; the probe sends no credentials or
   token.
2. The device browser receives `401` from the Mobile `/me` URL without a
   certificate warning.
3. The root appears in Android's user-installed CA list.
4. `wms-webapi-test` and `wms-caddy` are running on `xms-network`.
5. `docker logs wms-caddy --since 10m` shows the request.
6. The installed package is the Release ARM64 artifact above.
7. If Caddy logs an empty TLS `ServerName`, retain `default_sni vm-xms-dev`.

Clearing application data is not a network diagnostic.

## Rollback

Set `WMS_TAG` to a previously published compatible tag and recreate the affected
application containers. Database rollback is a separate decision and is safe
only when that application version supports the current schema.

Stopping Caddy preserves HTTP access for 1C and retains the CA volumes:

```powershell
docker compose -f docker-compose-caddy.yml down
```
