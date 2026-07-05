# Yedek servisi — Postgres + MySQL istemcileri build-time kurulu (runtime apk yok).
FROM alpine:3.20

# pg_dump (postgresql-client) + mysqldump (mariadb-client).
RUN apk add --no-cache postgresql16-client mariadb-client

COPY backup.sh /usr/local/bin/backup.sh
RUN chmod +x /usr/local/bin/backup.sh

ENTRYPOINT ["sh", "/usr/local/bin/backup.sh"]
