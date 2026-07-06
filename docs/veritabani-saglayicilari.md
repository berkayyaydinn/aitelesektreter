# Veritabanı Sağlayıcıları (SQLite / PostgreSQL / MySQL)

Backend `DB_PROVIDER` ortam değişkeniyle üç sağlayıcıdan biriyle çalışır. Seçim
`backend/src/VoiceReception.Api/Program.cs` içinde yapılır; iş kodu sağlayıcıdan bağımsızdır.

## Karşılaştırma

| | `sqlite` (varsayılan) | `postgres` | `mysql` |
|---|---|---|---|
| Amaç | Lokal geliştirme/test | Üretim (önerilen) | Üretim alternatifi |
| Şema | `EnsureCreated` (migration yok) | `Migrate` (`Migrations/`) | `Migrate` (`Migrations/MySql/`) |
| Bağlantı | `SQLITE_PATH` | `DATABASE_URL` (zorunlu) | `DATABASE_URL` (zorunlu) |
| Randevu çakışma (TOCTOU) koruması | Yok (tek kullanıcı) | `appt_no_overlap` exclusion constraint (GiST, atomik) | `GET_LOCK` tenant kilidi (`MySqlBookingLock`) |
| EF paketi | Microsoft.EntityFrameworkCore.Sqlite | Npgsql.EntityFrameworkCore.PostgreSQL | Pomelo.EntityFrameworkCore.MySql |

## MySQL'e özel notlar

- **Bağlantı dizesi (Pomelo formatı):**
  `Server=127.0.0.1;Port=3306;Database=telesekreter;User=telesekreter;Password=...`
- **`MYSQL_SERVER_VERSION`** (örn. `8.0.36`) verilirse startup sürümü DB'ye bağlanmadan bilir;
  boşsa `ServerVersion.AutoDetect` bağlanıp sorar. Compose stack'i `mysql:8` kullandığından
  varsayılan `8.0.36` uygundur.
- **Çakışma koruması:** Postgres'teki `EXCLUDE USING gist` MySQL'de yok. Bunun yerine
  `SchedulingService.CreateAppointmentAsync` randevu oluştururken tenant başına
  `GET_LOCK('appt:<tenantId>', 5)` alır (bkz. `Scheduling/MySqlBookingLock.cs`):
  overlap kontrolü + insert kilit içinde çalışır, eşzamanlı çifte rezervasyon penceresi kapanır.
  Kilit 5 sn içinde alınamazsa güvenli taraf seçilir ve arayana **Conflict** döner.
  Bağlantı kopması durumunda MySQL session kilidini kendiliğinden bırakır (sızıntı olmaz).
- **Postgres → MySQL veri taşıma bu kapsamda değildir**; şema migration'ları sıfırdan kurulum içindir.

## Migration üretimi (iki context)

MySQL migration'ları Postgres setinden ayrı yaşar: `AppDbContext` → `Migrations/`,
`MySqlAppDbContext` → `Migrations/MySql/` (kendi snapshot'ı ile). **Her şema değişikliğinde
iki migration üretilmeli:**

```bash
cd backend/src/VoiceReception.Api

# 1) Postgres (canonical set)
dotnet ef migrations add <Ad> --context AppDbContext

# 2) MySQL (design-time factory sayesinde canlı DB istemez)
dotnet ef migrations add <Ad> --context MySqlAppDbContext --output-dir Migrations/MySql
```

`MySqlDesignTimeFactory` sabit sürüm + dummy bağlantı kullanır; migration üretimi
çalışan bir MySQL gerektirmez.

## Compose ile MySQL'e geçiş

Stack'te `mysql:8` servisi zaten var (Mirbal CRM paylaşımlı). `infra/compose/.env`:

```env
BACKEND_DB_PROVIDER=mysql
BACKEND_DATABASE_URL="Server=mysql;Port=3306;Database=telesekreter;User=telesekreter;Password=..."
```

Backend açılışta `Migrate()` çalıştırır; `/health` yanıtında `db: mysql` görünmeli.

## Gerçek MySQL entegrasyon testi (opsiyonel)

`MYSQL_TEST_URL` ortam değişkeni verilirse eşzamanlı rezervasyon yarış testi gerçek
MySQL'e karşı koşar (yoksa sessizce atlanır):

```powershell
$env:MYSQL_TEST_URL = "Server=127.0.0.1;Port=3306;Database=telesekreter_test;User=root;Password=..."
dotnet test backend/tests/VoiceReception.Tests
```
