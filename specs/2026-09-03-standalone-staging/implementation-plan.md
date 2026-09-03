# Implementation plan

1. Parameterize simultaneous dev and test application containers and protect
   local environment files.
2. Add one non-disruptive Caddy instance and persistent internal CA.
3. Establish certificate installation and manually verify HTTPS from desktop
   and Android browsers.
4. Make the Mobile API endpoint configurable without disabling certificate
   validation.
5. Persist ASP.NET Data Protection keys across application-container
   recreation.
6. Complete the standalone publish, migration, deployment, verification, and
   rollback guide.
