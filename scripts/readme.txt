
docker compose --env-file test.env -f docker-compose.yml up -d --force-recreate
docker compose --env-file dev.env -f docker-compose.yml up -d --force-recreate

docker compose -f docker-compose-caddy.yml up -d
docker cp wms-caddy:/data/caddy/pki/authorities/local/root.crt .\wms-caddy-root.crt
