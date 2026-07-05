# Uygulama Dokümantasyonu — İskele Durumu

Bu belge, oluşturulan iskelenin **ne içerdiğini**, **nasıl çalıştığını** ve **sıradaki adımları**
özetler. Yaklaşım: *erken spike + swappable soyutlama + pilot tenant*.

## Ne Yapıldı (İskele)

Çok-kiracılı AI telesekreterin **MVP çekirdeği** kuruldu: gelen çağrı → Türkçe diyalog →
randevu/sipariş → PostgreSQL → görüşme sonrası WhatsApp bilgilendirme.

İki servis, swappable dış bağımlılıklar, uyum kancaları yerinde. Henüz canlı sağlayıcı
anahtarları/POC bağlanmadı — kod sözleşmeleri ve akış hazır.

## Dosya Haritası

```
ai-telesekreter/
├── README.md                      Genel bakış + hızlı başlangıç
├── .env.example                   Ortak iç sır referansı
├── docs/
│   ├── architecture.md            Mimari, veri modeli, internal API sözleşmesi
│   ├── risk-mitigation.md         4 ana risk + minimize + fallback
│   ├── spikes.md                  Kod öncesi 5 doğrulama spike'ı
│   ├── onboarding-call-forwarding.md   Operatör **21*DID# talimatları + tradeoff
│   ├── local-testing.md           Postgres/Docker/LiveKit/Meta'sız lokal test
│   ├── compliance-iys.md          Giden kampanya + İYS onay/saat kapısı
│   ├── legal-ip.md                Marka/patent çerçevesi + teknik farklılaştırma + codename
│   └── IMPLEMENTATION.md          (bu dosya)
├── infra/livekit/                 SIP inbound trunk + dispatch rule + README
├── voice-agent/                   PYTHON — LiveKit Agents ses worker
│   ├── agent.py                   Giriş: çağrı karşıla, tenant config, KVKK anonsu, diyalog
│   ├── config.py                  .env yükleme (sır kodda yok)
│   ├── backend_client.py          Internal API istemcisi (httpx)
│   ├── tools.py                   LLM araçları: availability / appointment / order
│   └── providers/                 SWAPPABLE STT/LLM/TTS fabrikası
└── backend/                       .NET 8 Minimal API + EF Core (PostgreSQL)
    └── src/VoiceReception.Api/
        ├── Program.cs             DI + endpoint kayıt
        ├── Domain/                9 entity (tenant, did, randevu, sipariş, consent, ...)
        ├── Data/AppDbContext.cs   EF Core + indeksler (DID unique, randevu zaman)
        ├── Scheduling/            Uygunluk + çakışmasız randevu (iş kuralı tek yerde)
        ├── Messaging/             IMessagingProvider + WhatsApp Cloud (swappable)
        └── Endpoints/             Internal API (worker) + Tenant API (onboarding)
```

## Çalışma Mantığı (uçtan uca)

1. **Onboarding** — `POST /api/tenants` → tenant + DID yaratır, `**21*DID#` talimatı döner.
2. **Yönlendirme** — İşletme GSM'inde kodu çevirir; test çağrısı → `verify` ile `Active`.
3. **Gelen çağrı** — Müşteri arar → SIP trunk → LiveKit SIP → dispatch → `agent.py`.
4. **Tenant çözümü** — `agent.py` çağrılan DID'i okur → `GET /internal/tenants/by-did/{did}`.
5. **KVKK** — Kayıt anonsu çalınır, `call_started` + consent backend'e işlenir.
6. **Diyalog** — STT→LLM→TTS; LLM `check_availability` / `create_appointment` / `create_order`
   araçlarını çağırır → backend iş kuralını uygular (çakışma kontrolü).
7. **Bilgilendirme** — Randevu sonrası WhatsApp template (`IMessagingProvider`).

## Swappable Noktalar (kilitlenme yok)

| Katman | Soyutlama | Değiştirme |
|--------|-----------|------------|
| STT | `voice-agent/providers/factory.py` | `.env STT_PROVIDER` |
| LLM | aynı | `.env LLM_PROVIDER` / `LLM_MODEL` |
| TTS | aynı | `.env TTS_PROVIDER` |
| Mesajlaşma | `backend Messaging/IMessagingProvider` | `.env MESSAGING_PROVIDER` |
| Telefon | LiveKit SIP trunk (`infra/livekit/`) | provider IP/DID config |

## Çalıştırma

```bash
# Backend
cd backend && cp .env.example .env   # DATABASE_URL + INTERNAL_API_KEY doldur
dotnet restore
dotnet ef migrations add Init --project src/VoiceReception.Api   # ilk migration
dotnet ef database update --project src/VoiceReception.Api
dotnet run --project src/VoiceReception.Api

# Voice worker
cd voice-agent && cp .env.example .env   # LiveKit + provider anahtarları + BACKEND_BASE_URL
pip install -r requirements.txt
python agent.py dev
```

## Doğrulama (sıra)

Önce **spike'lar** (`docs/spikes.md`), sonra entegre test:
1. `GET /health` → `{ "status": "ok" }`.
2. `POST /api/tenants` → DID + yönlendirme talimatı döner.
3. Test SIM'de `**21*DID#` → DID'i ara → `agent.py` karşılıyor, KVKK anonsu duyuluyor.
4. Diyalogda randevu al → PostgreSQL `appointments` kaydı; aynı slotu tekrar dene → `conflict`.
5. Randevu sonrası WhatsApp template (Meta sandbox/pilot WABA) müşteriye ulaşıyor.

## Sıradaki Adımlar

**Tamamlandı (iskele doğrulandı):**
- [x] Backend build temiz (.NET 8, 0 hata/uyarı).
- [x] EF Core `Init` migration üretildi (`Migrations/`).
- [x] Randevu sonrası WhatsApp gönderimi `CreateAppointment` akışına bağlandı + MessageLog.
- [x] `agent.py` çağrı bitişinde `call_ended` webhook (shutdown callback).
- [x] Python — **22 pytest** geçti, coverage **~%83** (testable modüller). `voice-agent/tests/`.
- [x] Testler (.NET) — **20/20 geçti**, coverage **~%95 satır** (migrations hariç). `backend/tests/`:
  - SchedulingService birim, API integration (health/401/tam randevu/call_ended), İYS+CampaignRunner,
    kampanya HTTP akışı, provider'lar (console dialer/mesaj, WhatsApp token-yok + gönderim, TurkeyTime).
  - `coverlet.runsettings` migration'ları hariç tutar.
- [x] **Lokal mod alternatifleri** (dış bağımlılık olmadan test):
  - SQLite DB sağlayıcı (`DB_PROVIDER=sqlite`, otomatik şema) — Postgres/Docker gerekmez.
  - Console dry-run mesaj sağlayıcı (`MESSAGING_PROVIDER=console`) — Meta gerekmez.
  - Hizmet + çalışma saati yönetim uçları (dashboard MVP).
- [x] **Uçtan uca smoke test** (`scripts/smoke_test.py`) — çalışan backend'e HTTP, **15/15 geçti**.
  Dry-run WhatsApp bildirimi doğru parametrelerle tetiklendi (log doğrulandı).
- Rehber: `docs/local-testing.md`.

**Test edebileceğin nokta (ŞİMDİ):** `docs/local-testing.md` → backend'i çalıştır + `smoke_test.py`.

**Hemen (canlı bağlantı gerektiren — bu ortamda yoktu):**
- [ ] 5 spike'ı yeşile çek (`docs/spikes.md`) — özellikle SIP↔LiveKit ve Meta onayı (uzun sürer).
- [ ] PostgreSQL ayağa kaldır + `DB_PROVIDER=postgres` + `Migrate()` (otomatik startup'ta).
- [ ] Provider paketlerini kur (`pip install -r requirements.txt`) + canlı LiveKit ile `agent.py`.

## Sonradan Geliştirilebilir (Future Improvements)

Şu an iskele/MVP'de kapsam dışı; üretime giderken sıraya alınacak. Veri modeli ve soyutlamalar
bunların çoğuna zaten hazır.

### Özellik genişlemesi (ayrı spec/plan)
- **Çok-kiracılı WhatsApp** — Embedded Signup + per-tenant token. `WhatsAppCloudProvider` arkasında izole; şu an tek WABA/env.
- **Instagram Messaging** — `IMessagingProvider` yeni implementasyon (`channel="instagram"`).
- [x] **Giden kampanya + İYS iskelesi** — eklendi (`Domain/Campaign*`, `Compliance/`, `Outbound/`, `Endpoints/CampaignEndpoints`). İYS onay + izinli saat kapısı gerçek mantık + testli (4 test); arama dry-run. Detay: `docs/compliance-iys.md`. Üretim: gerçek İYS API + LiveKit outbound + arka plan kuyruğu.
- [x] **Telefonla fatura kesme** — eklendi (`Domain/Invoice`, `Invoicing/`, owner-auth `CreateInvoice`, voice `create_invoice` aracı + sahip modu). İki kat sahip doğrulama (worker + backend 403). Detay: `docs/invoicing.md`. Üretim: gerçek GİB/Paraşüt sağlayıcı + sahip kimliği güçlendirme.
- **Tenant dashboard** — mevcut uygulamaya modül: randevu/sipariş listesi, çağrı kayıtları, prompt/saat yönetimi (uçlar kısmen var).
- **Randevu yönetimi** — iptal/erteleme, hatırlatma mesajı (T-24s), takvim entegrasyonu (Google/Outlook).
- **Sipariş yapılandırması** — serbest metin yerine ürün kataloğu + fiyat + stok.

### Sağlamlaştırma (hardening)
- **İdempotensi** — randevu/mesaj için idempotency key (çift çağrı/yeniden deneme güvenliği).
- **Randevu çakışma yarışı** — Postgres'te `EXCLUDE`/unique constraint veya serializable tx (şu an app-seviyesi kontrol).
- **Internal API auth** — paylaşılan sır yerine mTLS veya kısa ömürlü JWT.
- **Webhook imza doğrulama** — Meta/LiveKit webhook'larında imza kontrolü.
- **Rate limiting** — tenant API + giden mesaj uçlarında.
- **Hata/gözlemlenebilirlik** — yapılandırılmış log, trace (OpenTelemetry), çağrı başarı metrikleri.
- **Saat dilimi** — şu an UTC; tenant bazlı TZ (Europe/Istanbul) ve DST.

### Ses kalitesi / maliyet
- **Provider eval otomasyonu** — STT WER + gecikme ölçüm seti (`docs/spikes.md` Spike 3'ü CI'a taşı).
- **Yerel açık model** — ölçek büyüyünce LLM/TTS self-host (maliyet düşürme).
- **Barge-in / kesme** — doğal diyalog için endpointing + interruption tuning.

### Test kapsamı
- [x] **Integration testleri** — `WebApplicationFactory` + izole SQLite ile gerçek HTTP (eklendi).
- [x] **Voice worker testleri** — eklendi: `pytest` 19 test (prompts, did, providers hata yolu, BackendClient MockTransport, config). Saf yardımcılar `prompts.py`/`did.py`'ye ayrıldı (livekit'siz test).
- **Yük testi** — eşzamanlı çağrı / DID routing.
- [x] **Kapsam ölçümü** — coverlet (.NET ~%95) + pytest-cov (Python ~%83). Hedef %80 aşıldı.
- **tools.py testi** — `@function_tool` livekit gerektirir; backend delegasyonu `BackendClient` testleriyle dolaylı kapsanır, doğrudan test için livekit kurulu CI gerekir.

## Notlar / Sınırlar

- Kod **iskele**: sağlayıcı SDK sürümleri/attribute anahtarları (ör. `sip.trunkPhoneNumber`) canlı
  LiveKit SIP kurulumunda doğrulanmalı (`_extract_did` tek yerde toplandı, kolay düzeltilir).
- WhatsApp MVP'de tek WABA token (env). Çok-kiracılı geçiş `WhatsAppCloudProvider` arkasında izole.
- Tüm sırlar `.env`'de, koda gömülü değil. `INTERNAL_API_KEY` iki serviste aynı olmalı.
