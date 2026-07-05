# VoiceReception — Sesli Ajan SaaS (codename)

Çok-kiracılı (multi-tenant) yapay zeka sesli karşılama platformu. İşletmeler **mevcut GSM
numaralarını ek numara almadan** çağrı yönlendirme ile yapay zeka karşılamaya bağlar. AI sesli ajan
müşteriyle Türkçe konuşur, **randevu / sipariş** alır ve görüşme sonrası **WhatsApp/Instagram**
üzerinden bilgilendirir.

> **Durum:** İskele (MVP odaklı). De-risk yaklaşımı: *erken spike (POC) + swappable soyutlama + pilot tenant*.

> **Codename / marka notu:** `VoiceReception` yalnızca **iç geliştirme codename**'idir, ürün markası
> değildir. Betimleyici terimler (ör. "telesekreter", "sesli karşılama") bilinçli olarak jenerik
> kullanılır — kimsenin markası değildir. **Nihai ürün/marka adı seçilmeden önce TÜRKPATENT marka
> araması + IP avukatı kliring şarttır.** Bkz. `docs/legal-ip.md`.

## Mimari (özet)

```
Müşteri → İşletme GSM ──(**21*DID#)──► TR SIP trunk ──► LiveKit SIP ──► Python Voice Worker
                                                                              │ tool-calling (HTTP)
                                                                              ▼
                                                                        .NET Backend
                                                              (tenant, randevu, sipariş, Meta, KVKK)
                                                                              │
                                                                        PostgreSQL
```

İki servis, bilinçli dil ayrımı:

| Servis | Dil | Sorumluluk |
|--------|-----|------------|
| `backend/` | .NET / C# | İş mantığı: tenant, randevu/sipariş, Meta mesaj, uyum, internal API |
| `voice-agent/` | Python | LiveKit Agents ses hattı: STT → LLM → TTS, backend'e tool çağrıları |

## De-Risk Prensipleri (koda gömülü)

1. **Swappable provider soyutlaması** — STT / LLM / TTS ve Mesajlaşma sağlayıcıları interface
   arkasında, `.env` ile değiştirilebilir. Hiçbir dış bağımlılığa kilitlenme yok.
2. **Spike-first** — SIP↔LiveKit, Türkçe STT/TTS ve Meta onayı kod yazmadan POC ile kanıtlanır.
   Bkz. `docs/spikes.md`.
3. **Pilot tenant** — Uçtan uca tek işletmeyle kanıtla, sonra ölçekle. Tenant config DID üzerinden.

## Klasörler

- `backend/` — .NET 8 Minimal API + EF Core (PostgreSQL)
- `voice-agent/` — Python LiveKit Agents worker + swappable provider'lar
- `infra/livekit/` — LiveKit SIP inbound trunk + dispatch kuralları
- `docs/` — mimari, risk azaltma, spike rehberi, çağrı yönlendirme talimatları

## Kurulum Dokümanları

- **[docs/netsantral-setup.md](docs/netsantral-setup.md)** — Netsantral (Netgsm bulut santral) telefon
  hattı: Custom (Özel) API webhook + SIP Trunk hibrit kurulumu, çağrı yönlendirme, doğrulama.
- **[docs/llm-setup.md](docs/llm-setup.md)** — LLM (diyalog beyni) ayarı: yerel / OpenAI-uyumlu
  self-host (vLLM / Ollama / LM Studio), model seçimi, tool-calling doğrulama, buluta geri dönüş.

## Hızlı Başlangıç

### Hemen test et (Postgres / Docker / LiveKit / Meta GEREKMEZ)

Lokal mod: SQLite + console dry-run mesaj. Detay: **`docs/local-testing.md`**.

```bash
# Backend (varsayılan sqlite + console)
cd backend
INTERNAL_API_KEY=test-key ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/VoiceReception.Api

# Başka terminalde: uçtan uca smoke test (LiveKit'siz, voice worker sözleşmesini taklit eder)
INTERNAL_API_KEY=test-key python scripts/smoke_test.py     # -> 17 geçti, 0 başarısız

# Birim testler
cd backend && dotnet test tests/VoiceReception.Tests/VoiceReception.Tests.csproj   # -> 12 geçti
```

### Tam kurulum (canlı)

Önce **spike**'ları doğrula (`docs/spikes.md`), sonra gerçek bağımlılıklara geç:

```bash
# Backend: .env -> DB_PROVIDER=postgres + DATABASE_URL, MESSAGING_PROVIDER=whatsapp_cloud + token
cd backend && cp .env.example .env && dotnet run --project src/VoiceReception.Api

# Voice worker
cd voice-agent && cp .env.example .env   # LiveKit + STT/LLM/TTS + backend URL
pip install -r requirements.txt
python agent.py dev
```

## MVP Kapsamı

Gelen çağrı → Türkçe diyalog → randevu/sipariş → PostgreSQL → görüşme sonrası WhatsApp bilgilendirme.

**İskele eklendi (üretim entegrasyonu bekliyor):** giden kampanya + İYS, telefonla fatura kesme.
**Ertelenen:** Instagram Messaging, gelişmiş dashboard.

## Uyum

- Çağrı başında **kayıt onayı anonsu** (KVKK). `consents` tablosuna işlenir.
- Giden kampanya (ertelenmiş): **İYS onay sorgusu zorunlu** — veri modeli buna hazır.

Detay: `docs/architecture.md`, `docs/risk-mitigation.md`.
