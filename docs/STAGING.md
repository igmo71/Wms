# WMS standalone staging

This guide covers the current standalone `dev` and `test` deployment on
`vm-xms-dev`. It deliberately keeps the existing HTTP endpoints available for
1C and adds separate HTTPS endpoints through one Caddy container.

## Addresses

| Environment | Service | HTTP | HTTPS |
| --- | --- | --- | --- |
| dev | WebApi | `http://vm-xms-dev:8206` | `https://vm-xms-dev:8216` |
| dev | WebApp | `http://vm-xms-dev:8207` | `https://vm-xms-dev:8217` |
| test | WebApi | `http://vm-xms-dev:8306` | `https://vm-xms-dev:8316` |
| test | WebApp | `http://vm-xms-dev:8307` | `https://vm-xms-dev:8317` |

The test Mobile API address is therefore:

```text
https://vm-xms-dev:8316/
```

## Local environment files

`scripts/dev.env` and `scripts/test.env` contain real deployment settings and
are excluded from Git. Create them from `scripts/dev.env.example` and
`scripts/test.env.example`. The two files must have different
`WMS_ENVIRONMENT`, host ports, and WMS database names.

```powershell
Copy-Item dev.env.example dev.env
Copy-Item test.env.example test.env
```

The environment variables are passed explicitly by `docker-compose.yml`.
Merely placing a value in an env file does not automatically create the
corresponding ASP.NET configuration key inside a container.

## Refresh application containers

From the `scripts` directory on the Docker host:

```powershell
docker compose --env-file dev.env -f docker-compose.yml up -d --force-recreate
docker compose --env-file test.env -f docker-compose.yml up -d --force-recreate
```

Confirm that all four application containers are attached to `xms-network`:

```powershell
docker network inspect xms-network
```

The expected container names are:

```text
wms-webapi-dev
wms-webapp-dev
wms-webapi-test
wms-webapp-test
```

## Start Caddy

Copy `Caddyfile` and `docker-compose-caddy.yml` into the same `scripts`
directory on the Docker host, then run:

```powershell
docker compose -f docker-compose-caddy.yml up -d
docker compose -f docker-compose-caddy.yml logs caddy
```

Caddy must report all four HTTPS servers without upstream-resolution errors.
A `502 Bad Gateway` usually means that the target application container is not
running under the expected name or is not attached to `xms-network`.

Do not use `docker compose down -v` for this compose project. Removing the
`wms-caddy-data` volume deletes the local CA and forces every client to trust a
new certificate.

## Export and trust the root CA

Export the public root certificate from Caddy:

```powershell
docker cp wms-caddy:/data/caddy/pki/authorities/local/root.crt .\wms-caddy-root.crt
```

The `.crt` file is public and may be distributed to clients. Never copy or
distribute the CA private key from the Caddy data volume.

To trust the CA on a Windows workstation, open an elevated terminal in the
same directory and run:

```powershell
certutil -addstore -f Root .\wms-caddy-root.crt
```

On the Android staging device, use the system certificate installation screen
to install `wms-caddy-root.crt` as a CA certificate. Menu wording varies by
vendor; it is normally under Security, Encryption and credentials, Install a
certificate, CA certificate. The Android warning that network traffic may be
monitored is expected for a manually installed private CA.

## Manual verification

After installing the CA, open these addresses in a browser:

```text
https://vm-xms-dev:8217
https://vm-xms-dev:8317
```

Both WebApp pages must open without a certificate warning. Then open:

```text
https://vm-xms-dev:8316/api/mobile/v1/me
```

`401 Unauthorized` is the expected unauthenticated response and proves that
the test WebApi is reachable through HTTPS.

The Android system does not normally expose user-installed CAs to applications.
The staging Mobile manifest explicitly permits that trust only for
`vm-xms-dev`; normal certificate-chain and host-name validation still applies.
Install `wms-caddy-root.crt` on the warehouse device as a CA certificate before
opening the updated app.

The Urovo Android 9 system HTTP client does not send SNI for the single-label
host name `vm-xms-dev`. Caddy therefore uses `default_sni vm-xms-dev` to select
the same managed certificate when SNI is absent. This does not bypass
certificate-chain or host-name validation on the device.

Install a `Release` Mobile build for autonomous testing. It uses
`https://vm-xms-dev:8316/` from the packaged configuration; the operator does
not enter or change the server address. A `Debug` build uses
`https://localhost:7249/` for the existing Visual Studio debugging path.

## Non-destructive rollback

Stopping Caddy leaves the existing HTTP deployment untouched:

```powershell
docker compose -f docker-compose-caddy.yml down
```

The named CA volumes remain and will be reused on the next start.
