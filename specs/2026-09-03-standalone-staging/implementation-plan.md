# Implementation plan

1. Parameterize simultaneous dev and test application containers and protect
   local environment files.
2. Add one non-disruptive Caddy instance and persistent internal CA.
3. Establish certificate installation and manually verify HTTPS from desktop
   and Android browsers.
4. Select the Mobile API endpoint from packaged Debug/Release configuration
   without operator input or disabled certificate validation. **Completed and
   manually verified on the Urovo terminal.**
5. Persist ASP.NET Data Protection keys across application-container
   recreation.
6. Complete the standalone publish, migration, deployment, verification, and
   rollback guide.
