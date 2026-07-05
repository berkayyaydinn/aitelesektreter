# Self-hosted Stack (tek sunucu, tek `up`)

Tek Docker Compose ile: PostgreSQL + MySQL (DB'ler), ai-telesekreter **backend** + **voice-agent**
(container), **Caddy** (reverse proxy + TLS) ve günlük **backup**. Mirbal CRM ayrı repoda native kalır
(MySQL'e bağlanır).

| Servis | Ne | Erişim |
|--------|-----|--------|
| `postgres` | telesekreter DB | iç ağ + 127.0.0.1:5432 |
| `mysql` | CRM DB | iç ağ + 127.0.0.1:3306 |
| `backend` | .NET API | iç ağ `backend:8080`, dışarı Caddy üzerinden |
| `voice-agent` | LiveKit worker | dış: LIVEKIT_URL'e bağlanır |
| `caddy` | reverse proxy + TLS | 80/443 |
| `backup` | pg_dump + mysqldump | `backups` volume |

## Önkoşul
- Sunucuda **Docker** + **Docker Compose**.
- Firewall: yalnız 80/443 dışarı açık; 5432/3306 kapalı (compose zaten `127.0.0.1`'e bağlar).

## 1. Tüm stack'i ayağa kaldır
```bash
cd infra/compose
cp .env.example .env          # TÜM şifre/anahtarları doldur (DB + INTERNAL_API_KEY + LIVEKIT_* + provider) — .env'i COMMIT ETME
docker compose up -d --build  # imajları derler + başlatır
docker compose ps             # postgres/mysql "healthy"; backend/voice-agent/caddy/backup "running"
```
- **backend** açılışta Postgres'e `Migrate()` → şema + `appt_no_overlap` constraint otomatik.
- **voice-agent** `LIVEKIT_URL`'e bağlanır, gelen çağrı job'larını işler.
- **caddy** `https://<sunucu-ip>` → backend (self-signed sertifika; tarayıcı uyarısı normal).

Veri named volume'da kalıcı (`pgdata`/`mysqldata`/`caddy_data`/`backups`). `down` veriyi silmez;
tümünü silmek için `down -v`.

### TLS / domain
`Caddyfile` şu an `tls internal` (domain yok → self-signed). Domain bağlayınca: `:443`'ü
`api.firma.com` yap, `tls internal`'ı sil → Caddy otomatik Let's Encrypt alır/yeniler. Sonra
`docker compose restart caddy`.

### Yedek
`backup` servisi günde bir `pg_dump` + `mysqldump` → `backups` volume (gzip), `BACKUP_RETENTION_DAYS`
(vars. 7) sonra siler. Dosyaları gör: `docker compose exec backup ls -lh /backups`.
Geri yükleme: `gunzip -c pg_<ts>.sql.gz | docker compose exec -T postgres psql -U telesekreter -d telesekreter`.

## 2. (Alternatif) backend'i container yerine native çalıştır
Yukarıdaki `up --build` backend'i zaten container'da koşturur. Native çalıştırmak istersen
(`compose.yml`'den `backend`/`voice-agent`'ı çıkar veya kullanma) `backend/.env`:
```dotenv
DB_PROVIDER=postgres
DATABASE_URL=Host=127.0.0.1;Port=5432;Database=telesekreter;Username=telesekreter;Password=<POSTGRES_PASSWORD>
```
```bash
cd backend && dotnet run --project src/VoiceReception.Api
curl -s localhost:5080/health      # "db":"postgres"
```

## 3. Mirbal CRM → MySQL (ayrı repo, native)
Bağlantıyı **env ile** ver (kod/appsettings'e şifre yazma). ASP.NET `ConnectionStrings__DefaultConnection`
override'ı `GetConnectionString("DefaultConnection")`'ı ezer:
```bash
export ConnectionStrings__DefaultConnection="Server=127.0.0.1;Port=3306;Database=mirbal;Uid=mirbal;Pwd=<MYSQL_PASSWORD>"
dotnet run    # CRM açılışta EnsureCreated ile şemayı kurar
```
(systemd kullanıyorsan bu satırı servis biriminin `Environment=` alanına koy.)

## 4. Mevcut CRM verisini taşı (opsiyonel)
CRM şu an uzak paylaşımlı MySQL'de veri tutuyorsa, yerel MySQL'e aktar:
```bash
# uzak host'tan dışa aktar
mysqldump -h <eski-host> -u <eski-user> -p <eski-db> > mirbal_dump.sql
# yerel container'a içe aktar
docker compose exec -T mysql mysql -u mirbal -p"<MYSQL_PASSWORD>" mirbal < mirbal_dump.sql
```

## 5. Güvenlik (zorunlu)
- **Sızan şifreyi döndür:** `DeCostle@DB#2024` repoda commit edilmişti → yanmış sayılır. Yeni MySQL'de
  farklı şifre kullan **ve** eski uzak host'taki şifreyi de değiştir.
- `.env` ve gerçek `appsettings` dosyalarını commit etme.
- DB portları 127.0.0.1'e bağlı; doğrula: `ss -tlnp | grep -E '5432|3306'` → yalnız `127.0.0.1`.

## 6. Yedek
`backup` servisi otomatik (yukarıda). Elle anlık yedek istersen:
```bash
docker compose exec -T postgres pg_dump -U telesekreter telesekreter | gzip > pg_$(date +%F).sql.gz
docker compose exec -T mysql mysqldump -u root -p"<MYSQL_ROOT_PASSWORD>" --all-databases | gzip > my_$(date +%F).sql.gz
```

## Doğrulama özet
- `docker compose ps` → postgres/mysql healthy; backend/voice-agent/caddy/backup running
- backend (container) `/health` → `db: postgres`. İç testten: `docker compose exec backend wget -qO- localhost:8080/health`
- `docker compose exec postgres psql -U telesekreter -d telesekreter -c '\d+ "Appointments"'` →
  `appt_no_overlap` constraint görünür (TOCTOU koruması aktif)
- `docker compose logs voice-agent` → LiveKit'e "registered worker" benzeri bağlantı logu
- `docker compose exec backup ls -lh /backups` → 24 saat içinde `pg_*.sql.gz` + `mysql_*.sql.gz`
