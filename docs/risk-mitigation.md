# Risk Azaltma & Süreçler

İlke: **riskler tamamen sıfırlanmaz**, ama *erken spike (POC) + swappable soyutlama + pilot tenant*
ile yönetilebilir hale gelir. Her dış bağımlılık bir interface arkasında, `.env` ile değiştirilebilir.

## 1. TR SIP trunk ↔ LiveKit SIP + DID temini

**Minimize**
- Standart SIP trunk + IP auth + DID veren sağlayıcı (Verimor / NetGSM).
- Codec'i **G.711 (PCMU/PCMA)** sabitle — TR sağlayıcı + LiveKit destekler.
- Commit'ten önce **telefon spike** (bkz. `spikes.md` #1): tek DID, LiveKit inbound trunk, INVITE bağla.

**Olmazsa (uyumsuzluk)**
- Araya **SBC/media gateway** (FreeSWITCH/Asterisk) veya hosted SIP köprü (ör. Telnyx) koy:
  `TR provider → SBC → LiveKit`. Neredeyse her uyumsuzluğu çözer; bedeli ekstra bir bileşen.

## 2. Operatör çağrı yönlendirme farkları

**Minimize**
- Varsayılan **koşulsuz** (`**21*DID#`) — Turkcell/Vodafone/Türk Telekom'da tutarlı.
- Koşullu (`**61/**62/**67`) operatör sesli mesajına düşebilir → varsayılan yapma.
- Kurulum sonrası **otomatik test-çağrı doğrulaması**: DID'i ara, gelen çağrı algılandı mı?

**Tradeoff/çözüm**
- Koşulsuz = TÜM aramalar AI'a → sahibi o hattan arama alamaz. Çözüm: **mesai modu toggle**
  veya ayrı iş hattı/eSIM önerisi. Bkz. `onboarding-call-forwarding.md`.

## 3. Türkçe STT/TTS kalitesi

**Minimize**
- STT/TTS **interface arkasında**, per-tenant değiştirilebilir (`voice-agent/providers/`).
- **8kHz dar bant** (telefon) ile test et — temiz mikrofonla değil.
- 20-50 gerçek Türkçe domain cümlesinden **eval seti**: WER + gecikme ölç (Deepgram nova-3 /
  Azure STT / Whisper large-v3). TTS: Azure Neural TR / ElevenLabs multilingual.

**Gecikme**
- Streaming STT + streaming TTS + hızlı LLM + endpointing tuning. Hedef **<~1.5 sn**.

## 4. Meta WhatsApp çok-kiracılı bağlama

**Minimize**
- **Tech Provider** ol + **Embedded Signup** → her tenant kendi WABA'sını uygulamadan bağlar.
- **Meta Business Verification** günler-haftalar → **dev ile paralel, hemen başlat**.

**MVP fallback**
- Onay beklerken tek WABA (pilot tenant) + onaylı **template**. **24 saat kuralı**: pencere dışı
  sadece template → randevu onayı zaten template; gün 1'den template'e göre tasarla.

## Genel De-Risk Akışı

```
Spike (kanıtla) ──► Swappable interface (kilitlenme) ──► Pilot tenant (uçtan uca) ──► Ölçekle
```
