# WMS standalone staging

This guide covers the independent `dev` and `test` environments on
`vm-xms-dev`. Both environments run outside Visual Studio, use separate WMS
databases and ports, and share one Caddy instance and its persistent internal
CA.

1C continues to use the existing HTTP WebApi endpoints. WebApp and Mobile use
HTTPS through Caddy.

## Deployment map

| Environment | Service | HTTP | HTTPS |
| --- | --- | --- | --- |
| dev | WebApi | `http://vm-xms-dev:8206` | `https://vm-xms-dev:8216` |
| dev | WebApp | `http://vm-xms-dev:8207` | `https://vm-xms-dev:8217` |
| test | WebApi | `http://vm-xms-dev:8306` | `https://vm-xms-dev:8316` |
| test | WebApp | `http://vm-xms-dev:8307` | `https://vm-xms-dev:8317` |

The standalone Mobile Release build always uses the test API:

```text
https://vm-xms-dev:8316/
```

The complete first-time deployment order is:

```text
environment files
  -> databases
  -> WebApi and WebApp containers
  -> Caddy
  -> root CA on clients
  -> Mobile APK on the warehouse device
  -> autonomous verification
```

## 1. Prepare environment files

On the Docker host, create the real files from the committed examples in the
`scripts` directory:

```powershell
Copy-Item dev.env.example dev.env
Copy-Item test.env.example test.env
```

Set the real values in both files. `dev.env` and `test.env` must have different
`WMS_ENVIRONMENT`, host ports, and WMS database names. They contain secrets,
are excluded from Git, and must not be copied back into the repository.

The variables are passed explicitly by `docker-compose.yml`. Merely placing a
value in an env file does not automatically create the corresponding ASP.NET
configuration key inside a container.

## 2. Initialize databases

The clean migration baseline consists of:

```text
CreateIdentitySchema
CreateInitialWmsSchema
```

From the repository root, create each empty database from the same application
version by supplying its connection string explicitly:

```powershell
dotnet ef database update --project Wms/Wms.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj --connection "<dev connection string>"
dotnet ef database update --project Wms/Wms.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj --connection "<test connection string>"
```

Do not reuse a database whose `__EFMigrationsHistory` belongs to the migration
chain that preceded this baseline. Delete and recreate that database first.
After the baseline has been established, apply future migrations normally and
do not edit or remove a migration that may already have been applied.

## 3. Publish application images

From the repository root on the development workstation:

```powershell
.\scripts\publish.ps1
```

The script publishes and pushes `wms-webapi` and `wms-webapp` with one generated
tag. A specific tag may be supplied when reproducibility is required:

```powershell
.\scripts\publish.ps1 -Tag "2026-09-04_18-30"
```

Put the resulting tag into `WMS_TAG` in the target environment file on the
Docker host.

## 4. Start or update WebApi and WebApp

From the `scripts` directory on the Docker host:

```powershell
docker compose --env-file dev.env -f docker-compose.yml up -d --force-recreate
docker compose --env-file test.env -f docker-compose.yml up -d --force-recreate
```

Confirm that the four application containers are running and attached to
`xms-network`:

```powershell
docker compose --env-file dev.env -f docker-compose.yml ps
docker compose --env-file test.env -f docker-compose.yml ps
docker network inspect xms-network
```

Expected container names:

```text
wms-webapi-dev
wms-webapp-dev
wms-webapi-test
wms-webapp-test
```

## 5. Start Caddy

Keep `Caddyfile` and `docker-compose-caddy.yml` in the same `scripts` directory
on the Docker host, then run:

```powershell
docker compose -f docker-compose-caddy.yml up -d
docker compose -f docker-compose-caddy.yml logs caddy
```

Caddy must report all four HTTPS servers without upstream-resolution errors.
A `502 Bad Gateway` normally means that the expected application container is
not running or is not attached to `xms-network`.

Do not run `docker compose down -v` for Caddy. Removing the `wms-caddy-data`
volume deletes the local CA and forces every client to trust a new certificate.

## 6. Export and trust the root CA

Export the public root certificate from Caddy on its first deployment or after
an intentional CA replacement:

```powershell
docker cp wms-caddy:/data/caddy/pki/authorities/local/root.crt .\wms-caddy-root.crt
```

The `.crt` file is public and may be distributed to clients. Never copy or
distribute the CA private key from the Caddy data volume.

### Windows workstation

Open an elevated terminal in the certificate directory and install it for the
local computer:

```powershell
certutil -addstore -f Root .\wms-caddy-root.crt
```

Firefox may use its own certificate store depending on workstation policy. If
it still reports `SEC_ERROR_UNKNOWN_ISSUER`, import the same certificate as a
trusted certificate authority in Firefox or enable its use of the Windows root
store.

### Android warehouse device

Connect the device with USB debugging enabled and copy the certificate:

```powershell
adb devices
adb push .\scripts\wms-caddy-root.crt /sdcard/Download/wms-caddy-root.crt
```

On the device, open the system certificate installation screen and install
`wms-caddy-root.crt` as a **CA certificate**, not as a VPN or application
certificate. Menu wording varies by vendor; it is normally under Security,
Encryption and credentials, Install a certificate, CA certificate. Android's
warning that network traffic may be monitored is expected for a manually
installed private CA.

The Mobile staging manifest trusts user-installed CAs only for `vm-xms-dev`.
Normal certificate-chain and host-name validation remains enabled. The Urovo
Android 9 system HTTP client does not send SNI for this single-label host, so
Caddy uses `default_sni vm-xms-dev` to select the correct certificate.

## 7. Build and install Mobile

The device must be connected to the warehouse Wi-Fi and must resolve and reach
`vm-xms-dev`. Before installing the app, verify in the device browser:

```text
https://vm-xms-dev:8316/api/mobile/v1/me
```

The expected unauthenticated response is `401 Unauthorized`. A certificate
warning means that the CA is not installed correctly; a connection error means
that DNS, Wi-Fi routing, the host firewall, Caddy, or WebApi must be fixed first.

Build the standalone ARM64 APK from the repository root:

```powershell
dotnet publish .\Wms.Mobile\Wms.Mobile.csproj -f net10.0-android -c Release -r android-arm64
```

Install or update it without removing the existing application data:

```powershell
adb install -r .\Wms.Mobile\bin\Release\net10.0-android\android-arm64\publish\ru.igmo.wms.mobile-Signed.apk
```

`Success` confirms installation. The Release build uses the packaged
`https://vm-xms-dev:8316/` address; the operator neither enters nor changes the
server address. A Debug build uses `https://localhost:7249/` and belongs only to
the Visual Studio debugging path.

If a clean application state is specifically required, clear it before
launching:

```powershell
adb shell pm clear ru.igmo.wms.mobile
```

This deletes the app's saved session and local data. It does not remove the
device CA certificate. It is not required for a normal `adb install -r` update.

## 8. Verify the complete deployment

Check WebApp from a workstation browser without certificate warnings:

```text
https://vm-xms-dev:8217
https://vm-xms-dev:8317
```

Then verify Mobile independently of the development workstation:

1. Check debugging tunnels with `adb reverse --list`. If `tcp:7249` is listed,
   remove it with `adb reverse --remove tcp:7249`.
2. Disconnect the USB cable.
3. Start Wms.Mobile on the warehouse device while it remains on Wi-Fi.
4. Sign in with a test WMS user.
5. Open scanner diagnostics and scan a known SKU.
6. Open one available operational queue or document.

A successful login and SKU lookup with the cable disconnected prove the path
`Mobile -> Wi-Fi/DNS -> Caddy -> test WebApi -> Wms_test`.

## Routine update

For an ordinary application update after the one-time host and CA setup:

1. Run `publish.ps1` and record its tag.
2. Set `WMS_TAG` in the target env file.
3. Apply any new EF migration to that environment's database.
4. Recreate that environment's WebApi and WebApp containers.
5. Rebuild and install the Mobile Release APK only when Mobile changed.
6. Perform the relevant WebApp and Mobile smoke checks.

Caddy and its CA do not need to be recreated for an ordinary update.

## Troubleshooting Mobile connectivity

If Mobile reports that the WMS server is unavailable, check in this order:

1. The device browser reaches `https://vm-xms-dev:8316/api/mobile/v1/me` and
   receives `401` without a certificate warning.
2. The certificate is listed among user-installed trusted CA certificates.
3. `wms-webapi-test` and `wms-caddy` are running and share `xms-network`.
4. Caddy logs show the device request:

   ```powershell
   docker logs wms-caddy --since 10m
   ```

5. The installed APK is the Release ARM64 artifact shown above.
6. Only after those checks, clear the application data and sign in again.

If Caddy logs a TLS handshake with an empty `ServerName`, retain
`default_sni vm-xms-dev` in `Caddyfile`; this is the verified Urovo Android 9
case.

## Rollback

To roll back WebApi and WebApp, set `WMS_TAG` to a previously published tag in
the affected environment file and recreate that environment's containers.
Database rollback is a separate explicit decision and is safe only when the
older application is compatible with the current schema.

Stopping Caddy leaves the HTTP deployment available to 1C:

```powershell
docker compose -f docker-compose-caddy.yml down
```

The named Caddy volumes remain and will be reused on the next start.
