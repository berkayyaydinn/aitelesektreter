# Hatırlatma Sistemi (SMS)

Yaklaşan randevuları ve geciken ödemeleri SMS ile hatırlatır. SMS sağlayıcı **swappable**:
`console` (dry-run, varsayılan) → `netgsm` (gerçek). Hat gelmeden tüm akış `console` ile çalışır.

## Bileşenler

| Parça | Dosya |
|-------|-------|
| SMS soyutlaması | `Messaging/Sms/ISmsProvider.cs` (`ConsoleSmsProvider`, `NetgsmSmsProvider`) |
| Arka plan dağıtıcı | `Reminders/ReminderDispatcher.cs` (`BackgroundService`) |
| Sessiz saat helper | `Reminders/ReminderWindow.cs` |
| Şema | `Appointment.ReminderSentAt`, `Invoice.{DueDate,PaymentStatus,ReminderSentAt}` |
| Rıza | `ConsentType.TransactionalSms` |

## Akış

`ReminderDispatcher` `REMINDER_SCAN_INTERVAL_MINUTES` aralığıyla tarar (PeriodicTimer + enjekte saat):

1. **Sessiz saat** — TR yerel (UTC+3) `REMINDER_QUIET_START`–`REMINDER_QUIET_END` dışındaysa tick atlanır (işaretleme yok, sonraki uygun tick tekrar dener).
2. **Randevu** — `Status=Booked`, `ReminderSentAt=null`, `nowUtc < StartUtc <= nowUtc + LEAD_HOURS`.
3. **Geciken ödeme** — `PaymentStatus=Unpaid`, `ReminderSentAt=null`, `DueDate < nowUtc`, telefon var.
4. **Rıza kapısı** — `REMINDER_REQUIRE_CONSENT=true` ise `TransactionalSms` rıza kaydı yoksa atla.
5. **Gönder + işaretle** — `MessageLog` + (yalnız başarıda) `ReminderSentAt` aynı transaction'da yazılır → idempotent (en-az-bir-kez).

## Yapılandırma (env)

```
SMS_PROVIDER=console            # console | netgsm
NETGSM_USERCODE=
NETGSM_PASSWORD=
NETGSM_MSGHEADER=               # Netgsm'de onaylı başlık

REMINDERS_ENABLED=true          # false -> dağıtıcı hiç kaydedilmez
REMINDER_SCAN_INTERVAL_MINUTES=5
REMINDER_APPOINTMENT_LEAD_HOURS=24
REMINDER_QUIET_START=09:00      # TR yerel
REMINDER_QUIET_END=21:00
REMINDER_REQUIRE_CONSENT=true   # false -> yalnız test
```

## Lokal test (Netgsm'siz)

```
DB_PROVIDER=sqlite SMS_PROVIDER=console REMINDERS_ENABLED=true \
REMINDER_SCAN_INTERVAL_MINUTES=1 REMINDER_REQUIRE_CONSENT=false \
dotnet run --project backend/src/VoiceReception.Api
```
`now+12h` bir randevu ekle → log'da `[DRY-RUN sms] -> ... randevunuzu hatırlatırız` görünür; ikinci tick'te tekrar gitmez (`ReminderSentAt` set).

## Netgsm'e geçiş

`.env`: `SMS_PROVIDER=netgsm` + `NETGSM_USERCODE/PASSWORD/MSGHEADER` doldur. Başlık Netgsm panelinde onaylı olmalı. Yanıt kodları (`00/01/02` başarı; `30` kimlik/IP, `40` başlık, `50/51` İYS …) `NetgsmSmsProvider.ErrorReasons`'da. Gerçek gövdeyle ilk gönderimde doğrula.

## Uyarılar
- **İYS sınıflandırması:** transactional SMS'in hukuki statüsü netleşmeli (`compliance-iys.md`). Prod'da açmadan `REMINDER_REQUIRE_CONSENT` ile kapıyı doğrula.
- **At-least-once:** HTTP başarı ile DB commit arası crash nadiren çift SMS doğurur (kayıp yerine kabul).
- **Çok-instance:** tek backend container varsayımı; yatay ölçekte satır kilidi (`FOR UPDATE SKIP LOCKED`) gerekir.
