
docker compose --env-file dev.env -f docker-compose.yml up -d --force-recreate
docker compose --env-file test.env -f docker-compose.yml up -d --force-recreate

docker compose -f docker-compose-caddy.yml up -d
docker cp wms-caddy:/data/caddy/pki/authorities/local/root.crt .\wms-caddy-root.crt

adb push .\wms-caddy-root.crt /sdcard/Download/
adb install -r .\bin\Debug\net10.0-android\ru.igmo.wms.mobile-Signed.apk
