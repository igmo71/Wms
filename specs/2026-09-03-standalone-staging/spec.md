# Standalone staging

Status: Active

## Outcome

Run the accepted WMS baseline independently of Visual Studio so WebApp and
Mobile can use the same backend from warehouse devices while development
continues against a separate environment.

## Scope

- run simultaneous `dev` and `test` container sets on `vm-xms-dev`;
- keep their WMS databases, container names, and host ports separate;
- retain the current HTTP endpoints used by 1C notifications;
- add one Caddy instance with a persistent internal CA and separate HTTPS
  entry points for both environments;
- select the Mobile API address from packaged debug and standalone build
  configuration without operator input;
- document publish, migration, deployment, certificate trust, verification,
  and rollback steps.

Production hardening, public exposure, durable 1C notification delivery, and
changes to the currently accepted diagnostic detail are outside this issue.

## Port map

| Environment | Service | Existing HTTP | Added HTTPS |
| --- | --- | --- | --- |
| dev | WebApi | `8206` | `8216` |
| dev | WebApp | `8207` | `8217` |
| test | WebApi | `8306` | `8316` |
| test | WebApp | `8307` | `8317` |

The existing HTTP ports remain available because 1C currently sends document
and catalog notifications to the dev WebApi on port `8206`.

## Acceptance criteria

- `dev` and `test` run simultaneously with independent configuration and WMS
  databases;
- existing 1C notification URLs continue to work;
- Android resolves `vm-xms-dev` and reaches both HTTPS environments without a
  certificate-validation bypass;
- recreating Caddy retains the same internal CA;
- a Debug Mobile build uses the local Visual Studio API address and a Release
  build uses the standalone test API address;
- deployment and recovery commands are reproducible outside Visual Studio.

## Open questions

- Whether the HTTP endpoints should later be retired after 1C trusts the local
  CA and its notification URLs move to HTTPS.
- How the staging CA certificate will be distributed to managed warehouse
  devices after the first manual installation.
