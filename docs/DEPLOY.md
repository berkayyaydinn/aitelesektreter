# VPS Dağıtım Rehberi (Self-Hosted)

Tüm sistem tek VPS'te `docker compose` ile çalışır: .NET backend, Python voice-agent,
self-host LiveKit (server + SIP + egress), self-host STT/TTS (Whisper + Piper), PostgreSQL +
MySQL, MinIO (kayıt), Caddy (TLS). Tek dış bağımlılık: OpenAI (LLM beyni) + Netgsm (SMS + telefon hattı).

> İlgili dokümanlar: [netgsm-setup.md](netgsm-setup.md) (SMS + trunk özet),
> [../infra/livekit/README.md](../infra/livekit/README.md) (LiveKit SIP),
> [onboarding-call-forwarding.md](onboarding-call-forwarding.md) (GSM yönlendirme alternatifi).

---

## 0. Önkoşullar

- Ubuntu 22.04+ VPS, public IP, root/sudo.
- Docker + Compose v2: `curl -fsSL https://get.docker.com | sh`
- Kaynak: pilot için min **4 vCPU / 8 GB RAM** (Whisper + LiveKit + egress aynı makinede; GPU yok).
- DNS (opsiyonel): `api.firma.com` A kaydı → VPS IP (Caddy otomatik TLS için).

## 1. Kodu al

```bash
git clone <repo> /opt/telesekreter
cd /opt/telesekreter/infra/compose
cp .env.example .env
```

## 2. Anahtarları üret

```bash
docker run --rm livekit/livekit-server generate-keys   # LIVEKIT_API_KEY + SECRET
openssl rand -hex 32                                    # INTERNAL_API_KEY
openssl rand -hex 24                                    # DB / MinIO şifreleri (her biri ayrı)
```

## 3. `.env` doldur (`infra/compose/.env`)

Zorunlu:
- `POSTGRES_PASSWORD`, `MYSQL_ROOT_PASSWORD`, `MYSQL_PASSWORD` — güçlü, benzersiz.
  **Eski sızan şifreyi kullanma.**
- `INTERNAL_API_KEY` — adım 2 (backend ↔ voice-agent ortak sır).
- `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` — adım 2. `LIVEKIT_URL` **boş bırak** (iç ağda
  `ws://livekit:7880` kullanılır).
- `OPENAI_API_KEY` — LLM beyni (tek zorunlu dış servis).
- STT/TTS varsayılan zaten self-host: `STT_PROVIDER=whisper`, `TTS_PROVIDER=piper`.

Opsiyonel:
- Kayıt: `RECORDING_ENABLED=true` + `RECORDING_S3_ACCESS_KEY` / `RECORDING_S3_SECRET`.
- SMS: `SMS_PROVIDER=netgsm` + `NETGSM_USERCODE/PASSWORD/MSGHEADER` (bkz. netgsm-setup.md).
- `RATE_LIMIT_PER_SECOND` (varsayılan 50) — anahtar/IP başına istek/sn.
- `INTERNAL_TENANT_KEYS="guid:key,guid:key"` — tenant'a kısıtlı admin anahtarları. CRM çok-tenant'a
  açılınca her işletmeye ayrı anahtar; scoped anahtar yalnız kendi `/api/tenants/{guid}/...` route'una
  erişir (başka tenant → 403). Boşsa sadece master `INTERNAL_API_KEY` (voice-agent kısıtsız).

`.env` **asla commit edilmez** (gitignore'da).

> **Tenant silme:** `DELETE /api/tenants/{tid}` (X-Internal-Key) soft-delete yapar — kayıt
> DB'de kalır (geçmiş korunur) ama tüm sorgulardan gizlenir ve DID routing durur.

## 4. Firewall (UFW)

```bash
ufw allow 22/tcp
ufw allow 80,443/tcp                      # Caddy
ufw allow 5060/udp; ufw allow 5060/tcp    # SIP (adım 7'de yalnız Netgsm IP'sine daralt)
ufw allow 10000:20000/udp                 # RTP (ses medyası)
ufw allow 50000:60000/udp                 # WebRTC medya
ufw enable
```

**Açılmaz (iç ağ):** PostgreSQL, MySQL, Redis, Whisper, Piper, MinIO (9000/9001), egress.

## 5. Ayağa kaldır

```bash
docker compose up -d
docker compose ps
docker compose logs -f whisper-stt    # ilk açılış: model indirir (yavaş), sonra cache'ten
```

## 6. Yerel sağlık kontrolü (iç ağdan)

```bash
docker compose exec backend wget -qO- http://localhost:8080/health   # {"status":"ok","db":"postgres"}
docker compose exec voice-agent curl -s http://whisper-stt:8000/health
docker compose exec voice-agent curl -s http://piper-tts:8000/v1/models
```

## 7. Netgsm / Netsantral bağlama (telefon hattı)

> Netsantral = Netgsm'in bulut santral ürünü. Gelen çağrıyı kendi self-host LiveKit'imize SIP ile
> taşırız. **Panel menü adları Netgsm'in arayüzüne göre değişebilir; teknik sözleşme (aşağıdaki
> alanlar) sabittir — Netgsm destek ile teyit et.**

### 7.1 Hat al
Netgsm'den şunları iste:
- **SIP trunk** (Netsantral SIP / santral SIP hattı) + en az bir **DID** (0850/0212 numara).
- Tercihen **IP tabanlı auth** (registration'sız): Netgsm, VPS public IP'ni izinli yapar ve
  çağrıyı doğrudan `VPS_IP:5060`'a INVITE eder.
- Alternatif: **kayıt tabanlı (register)** — Netgsm SIP `username/password/host` verir.

### 7.2 Netgsm'e iletilecek teknik bilgi (gelen çağrı hedefi)
- Hedef SIP host: **`VPS_PUBLIC_IP`**, port **5060** (UDP).
- Codec: **G.711 (PCMU/PCMA / alaw-ulaw)** — TR sağlayıcı + LiveKit uyumu.
- DTMF: **RFC 2833** (telephone-event).
- Media encryption: **kapalı** (SRTP yok).
- Netsantral panelinde ilgili **numaranın çağrı yönlendirme / SIP hedef** kuralını bu host'a kur.

### 7.3 Netgsm SIP sunucu IP'lerini al → trunk'a yaz
Netgsm'in çağrı gönderdiği SIP imza IP'lerini iste, `infra/livekit/inbound-trunk.json`:
- `allowed_addresses`: `["NETGSM_SIP_IP/32", ...]`
- `numbers`: `["+90850XXXXXXX", ...]` (aldığın DID'ler)

### 7.4 LiveKit'e inbound trunk + dispatch uygula
```bash
# lk CLI: https://github.com/livekit/livekit-cli
export LIVEKIT_URL=ws://localhost:7880
export LIVEKIT_API_KEY=...  LIVEKIT_API_SECRET=...
lk sip inbound create ../livekit/inbound-trunk.json
lk sip dispatch create ../livekit/dispatch-rule.json
```
Çağrılan DID, `sip.trunkPhoneNumber` attribute'unda agent'a geçer → `agent.py` DID'i okur →
backend `GET /internal/tenants/by-did/{did}` ile tenant'ı bulur.

### 7.5 Kayıt tabanlı (register) ise — outbound trunk auth
IP auth yoksa Netgsm `username/password` verir. `infra/livekit/outbound-trunk.json` oluştur
(giden arama + bazı sağlayıcılarda gelen için kimlik):
```json
{
  "trunk": {
    "name": "netgsm-outbound",
    "address": "NETGSM_SIP_HOST",
    "numbers": ["+90850XXXXXXX"],
    "auth_username": "NETGSM_SIP_USER",
    "auth_password": "NETGSM_SIP_PASS"
  }
}
```
```bash
lk sip outbound create ../livekit/outbound-trunk.json   # dönen trunk id'yi not et
```
`.env`'e giden arama için: `OUTBOUND_DIALER=livekit`, `NETGSM_SIP_OUTBOUND_TRUNK_ID=<trunk id>`.

### 7.6 GSM yönlendirme alternatifi
Cep hattını SIP yerine `**21*DID#` ile yönlendirme: [onboarding-call-forwarding.md](onboarding-call-forwarding.md).
Netsantral SIP trunk varken bu gerekmez.

## 8. Tenant oluştur (DID → işletme eşleme)

```bash
curl -X POST http://localhost:8080/api/tenants \
  -H "X-Internal-Key: $INTERNAL_API_KEY" -H "Content-Type: application/json" \
  -d '{"businessName":"Test İşletme","did":"08501234567"}'
# dönen tenantId (Guid) → CRM Telesekreter paneline gir
```

## 9. Uçtan uca test

DID'i ara → KVKK anonsu + Türkçe ajan. Doğrula:

**Çağrı okuma API ile (önerilen — HTTP, X-Internal-Key):**
```bash
TID=<tenant guid>
# Çağrı listesi + analitik (süre, endReason, outcome, toolCallCount, hasRecording)
docker compose exec backend wget -qO- --header="X-Internal-Key: $INTERNAL_API_KEY" \
  "http://localhost:8080/api/tenants/$TID/calls?limit=10"
# Tek çağrı detayı + transkript turları + recordingUrl
docker compose exec backend wget -qO- --header="X-Internal-Key: $INTERNAL_API_KEY" \
  "http://localhost:8080/api/tenants/$TID/calls/<callLogId>"
```

**Doğrudan DB ile (alternatif):**
```bash
docker compose exec postgres psql -U telesekreter -d telesekreter \
  -c 'select did, duration_seconds, end_reason, outcome, recording_url from "CallLogs" order by "StartedAt" desc limit 5;'
docker compose exec postgres psql -U telesekreter -d telesekreter \
  -c 'select role, left(text,40) from "ConversationTurns" order by "OccurredAt" desc limit 10;'
```
Kayıt açıksa MinIO'da OGG:
```bash
docker compose exec minio-init mc ls local/telesekreter-recordings/recordings/ --recursive
```

## 10. TLS / domain (opsiyonel)

`infra/compose/Caddyfile`: `:443` → `api.firma.com {`, `tls internal` satırını sil →
Let's Encrypt otomatik. `docker compose restart caddy`.

---

## Sorun giderme

| Belirti | Bak |
|---|---|
| Çağrı düşmüyor | Netgsm INVITE `VPS_IP:5060`'a ulaşıyor mu (`docker compose logs livekit-sip`); firewall 5060; `allowed_addresses` Netgsm IP'sini içeriyor mu |
| Tek yönlü ses / sessizlik | RTP `10000-20000/udp` açık mı; LiveKit/SIP `use_external_ip: true`; NAT/public IP doğru mu |
| "Bilinmeyen DID" / 404 | `PhoneNumbers`'da DID var mı (adım 8); trunk `numbers` ile eşleşiyor mu |
| STT/TTS yanıt yok | `livekit-plugins-openai` `base_url` imzası sürüme uygun mu; `logs voice-agent`; whisper modeli indi mi |
| Kayıt boş | `RECORDING_ENABLED=true` mu; `logs voice-agent` "Kayıt başlatılamadı"; `livekit.api` egress proto alan adları sürüme uygun mu; `logs livekit-egress` |
| Whisper yavaş | `WHISPER_MODEL=base`/`tiny` düş; ölçekte GPU veya yönetilen STT'ye `.env` ile dön |
| Backend testleri 401 (lokal) | `backend/.env` varsa test config'ini eziyor — testten önce geçici taşı |

## Güvenlik notları
- Tüm sırlar yalnız `.env`'de; asla commit etme. `INTERNAL_API_KEY` appsettings.json'a yazılmaz.
- DB / Redis / MinIO portları iç ağda; dışa yalnız 80/443 + SIP/RTP/RTC.
- SIP 5060'ı hat bağlanınca yalnız Netgsm IP'sine daralt (`ufw`).
- Sızan eski şifreleri kullanma; yeni üret.
