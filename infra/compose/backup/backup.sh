#!/bin/sh
# Günlük yedek: PostgreSQL (pg_dump) + MySQL (mysqldump) → /backups, gzip'li, RETENTION_DAYS sonra silinir.
# İstemciler imaja build-time kurulu (bkz. Dockerfile). Hostlar compose servis adları (postgres/mysql).
set -eu

RETENTION_DAYS="${RETENTION_DAYS:-7}"

echo "[backup] başladı, retention=${RETENTION_DAYS} gün"

while true; do
	ts="$(date +%F_%H%M)"

	echo "[backup] ${ts} postgres dump"
	PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h postgres -U "$POSTGRES_USER" "$POSTGRES_DB" \
		| gzip > "/backups/pg_${ts}.sql.gz" || echo "[backup] postgres dump HATA"

	echo "[backup] ${ts} mysql dump"
	mysqldump -h mysql -u root -p"$MYSQL_ROOT_PASSWORD" --all-databases \
		| gzip > "/backups/mysql_${ts}.sql.gz" || echo "[backup] mysql dump HATA"

	# Eski yedekleri buda.
	find /backups -name '*.sql.gz' -mtime "+${RETENTION_DAYS}" -delete 2>/dev/null || true

	# 24 saat bekle.
	sleep 86400
done
