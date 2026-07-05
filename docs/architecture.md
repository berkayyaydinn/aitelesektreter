# Mimari

## Genel Akış

```
Müşteri telefonu
   │ arar
   ▼
İşletme GSM numarası ──(çağrı yönlendirme **21*DID#)──► TR SIP sağlayıcı (DID / SIP trunk)
                                                              │ SIP INVITE
                                                              ▼
                                                  LiveKit SIP (inbound trunk)
                                                  DID → tenant eşleme + oda + agent dispatch
                                                              │
                    ┌──────────── Python Voice Worker (LiveKit Agents) ───────────┐
                    │  STT(TR) → LLM(hızlı bulut) → TTS(TR)                        │
                    │  Tool-calling ──HTTP(INTERNAL_API_KEY)──► .NET Backend       │
                    └──────────────────────────────────────────────────────────────┘
                                                              │
        .NET Backend ◄── webhook: çağrı bitti / transkript / kayıt URL
        • Tenant + DID yönetimi, onboarding
        • Randevu / Sipariş domain (PostgreSQL)
        • Meta WhatsApp / Instagram gönderim (swappable)
        • KVKK / İYS uyum kayıtları
```

## Servis Sınırları

### Python Voice Worker (`voice-agent/`)
- **Tek sorumluluk:** ses hattını sürmek. İş kararı vermez — backend'e sorar.
- Pipeline: VAD (Silero) → STT → LLM (tool-calling) → TTS.
- Çağrı başında DID'den tenant config çeker (prompt, çalışma saati, hizmetler).
- Araçlar backend internal API'sini çağırır: `check_availability`, `create_appointment`,
  `create_order`, `lookup_customer`.
- **Provider'lar swappable** (`providers/`): STT/LLM/TTS `.env` ile seçilir.

### .NET Backend (`backend/`)
- **Tek doğruluk kaynağı:** tüm iş verisi ve kararları.
- Internal API (voice worker tüketir) + Webhook alıcısı + Tenant yönetim API.
- Mesajlaşma soyutlaması (`IMessagingProvider`) → WhatsApp Cloud bugün, Instagram yarın.
- Uyum: çağrı kaydı onayı, KVKK, (ileride) İYS.

## Veri Modeli (PostgreSQL)

| Tablo | Amaç |
|-------|------|
| `tenants` | İşletme + abonelik |
| `phone_numbers` | DID ↔ tenant, yönlendirme durumu |
| `services` | Randevu/sipariş hizmet kalemleri |
| `business_hours` | Haftalık çalışma saatleri |
| `appointments` | Randevular |
| `orders` | Siparişler |
| `customers` | Arayan müşteriler |
| `call_logs` | Çağrı meta + transkript referansı |
| `consents` | KVKK kayıt onayı, (ileride) İYS onayı |
| `message_logs` | Giden WhatsApp/Instagram mesajları |

## DID → Tenant Routing

1. Tenant kaydolur → backend ona bir **DID** tahsis eder (`phone_numbers`).
2. Tenant kendi GSM'inde `**21*DID#` ile yönlendirme açar (`docs/onboarding-call-forwarding.md`).
3. Müşteri arar → SIP trunk INVITE'ı LiveKit'e → inbound trunk **çağrılan DID**'i okur →
   backend'den `GET /internal/tenants/by-did/{did}` ile tenant config alınır.
4. Voice worker o tenant'ın prompt + hizmet + saatleriyle konuşur.

## Internal API Sözleşmesi (voice-agent ↔ backend)

Tümü `X-Internal-Key: INTERNAL_API_KEY` ister.

| Metot | Yol | Amaç |
|-------|-----|------|
| GET | `/internal/tenants/by-did/{did}` | Tenant config (prompt, saat, hizmet) |
| POST | `/internal/availability` | Uygun randevu slotları |
| POST | `/internal/appointments` | Randevu oluştur (çakışma kontrollü) |
| POST | `/internal/orders` | Sipariş oluştur |
| POST | `/internal/invoices` | Fatura kes (SADECE sahip: callerPhone == OwnerPhone, yoksa 403) |
| GET | `/internal/customers/{phone}` | Müşteri ara (tenant kapsamlı) |
| POST | `/internal/calls/events` | Çağrı olayı / transkript / onay webhook |

## Uyum Akışı

- Çağrı başında worker **kayıt anonsu** çalar; backend'e `consent: call_recording` işlenir.
- Giden kampanya (ertelenmiş): arama öncesi `consents` İYS onayı sorgulanır; saat kısıtı uygulanır.
