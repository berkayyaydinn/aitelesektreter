# Netgsm SIP'e Geçiş — Detaylı Kurulum Rehberi

Sistemi lokal/dry-run modundan **gerçek Netgsm SIP hattına** taşıma rehberi. Sonunda:
müşteri gerçek numarayı arar → KVKK anonsu çalar → Türkçe AI telesekreter randevu/sipariş alır →
WhatsApp/SMS bilgilendirme gider.

> İlgili dokümanlar: [netgsm-setup.md](netgsm-setup.md) (SMS + outbound),
> [netsantral-setup.md](netsantral-setup.md) (hibrit Custom API),
> [llm-setup.md](llm-setup.md) (LLM seçimi), `infra/livekit/README.md` (SIP detay),
> [spikes.md](spikes.md) (doğrulama spike'ları).

---

## 0. Mimari — neyi kuruyoruz?

```
Müşteri telefonu
      │  (PSTN/GSM araması)
      ▼
Netgsm SIP Trunk (DID: 0850XXXXXXX)
      │  SIP signaling (5060) + RTP ses (10000-20000/udp)
      ▼
VPS: livekit-sip (5060) ──► livekit server (7880)
      │  dispatch rule: "telesekreter" agent
      ▼
voice-agent (Python worker)
      │  DID → tenant lookup, KVKK anonsu, STT→LLM→TTS
      ▼
backend (.NET API) ──► PostgreSQL
      │  randevu/sipariş/çağrı logu
      ▼
WhatsApp / SMS bilgilendirme
```

İki geçiş yolu:

| Yol | Ne | Ne zaman |
|-----|----|----------|
| **A — Sadece SIP Trunk** | DID doğrudan trunk'a bağlı; webhook yok | Başlangıç için öner — en az parça, uçtan uca ses doğrulanır |
| **B — Netsantral hibrit** | Çağrı önce Custom API webhook'una düşer (açık/kapalı kararı, esnek yönlendirme), ses yine SIP trunk'tan | Yol A çalıştıktan sonra karar katmanı istenirse |

Bu rehber **Yol A**'yı uçtan uca anlatır; Yol B farkları §8'de.

---

## 1. Önkoşullar

- [ ] **VPS** (public statik IP) — Docker + Docker Compose kurulu. CPU-only yeterli
      (self-host Whisper/Piper CPU profiliyle çalışır); LLM self-host ise `docs/llm-setup.md`'ye bak.
- [ ] **Netgsm hesabı** — kurumsal başvuru tamam.
- [ ] (Önerilen) **Domain** — backend için `api.firma.com` (Caddy otomatik Let's Encrypt alır).
      Domain'siz self-signed ile de çalışır.
- [ ] Repo sunucuya klonlanmış.

---

## 2. Netgsm tarafı — SIP Trunk + DID

1. Netgsm'den **SIP Trunk** hizmeti + **DID numara** (0850...) talep et.
2. Kimlik doğrulama olarak **IP auth** iste ve **VPS'in public IP'sini Netgsm'e whitelist ettir**
   (Netgsm yalnız izinli IP'ye çağrı/ses gönderir; user/pass auth verirlerse §4'te
   `auth_username/auth_password` alanlarını kullan).
3. Netgsm'den şunları **not al**:
   - **Netgsm SIP sunucu IP'si** (gelen SIP paketlerinin kaynağı) → trunk `allowed_addresses` + firewall.
   - **DID numarası** → trunk `numbers` + tenant kaydı.
4. Kodek: **G.711 (PCMU/PCMA)** — trunk config'te varsayılan uyumlu (`media_encryption: DISABLE`).

> Aynı anda **SMS gönderici başlığı** (msgheader) talebini de başlat — onayı uzun sürer
> (hatırlatma SMS'leri için gerekecek, §9).

---

## 3. Sunucu stack'i ayağa kaldır

```bash
cd infra/compose
cp .env.example .env
```

`.env`'de doldurulacaklar (COMMIT ETME):

| Anahtar | Değer |
|---------|-------|
| `POSTGRES_PASSWORD`, `MYSQL_PASSWORD` | güçlü rastgele |
| `INTERNAL_API_KEY` | backend ↔ voice-agent ortak sır (uzun rastgele) |
| `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` | aşağıdaki komutla üret |
| STT/TTS/LLM sağlayıcı anahtarları | seçtiğin profile göre (self-host'ta gerekmez) |

```bash
# LiveKit anahtarı bir kez üret, çıktıyı .env'e koy
docker run --rm livekit/livekit-server generate-keys

# Tüm stack
docker compose up -d --build
docker compose ps   # postgres "healthy"; backend/voice-agent/livekit/livekit-sip "running"
```

Açılışta otomatik olanlar:
- **backend** → Postgres'e `Migrate()` (şema + `appt_no_overlap` + retention tabloları).
- **voice-agent** → `LIVEKIT_URL`'e bağlanır, `telesekreter` agent adıyla job bekler.
  Turn-detector + VAD model dosyaları image build'inde indirilmiştir (`Dockerfile` `download-files`).
- **minio-init** → kayıt bucket'ı + KVKK imha (ILM) kuralı (`RECORDING_RETENTION_DAYS`, vars. 180).

Sağlık kontrolü:

```bash
curl -s https://<domain-veya-ip>/health
# {"status":"ok","db":"postgres",...}
```

---

## 4. LiveKit SIP trunk + dispatch kuralı

`infra/livekit/inbound-trunk.json` düzenle:

```jsonc
{
  "trunk": {
    "name": "tr-sip-inbound",
    "numbers": ["+90850XXXXXXX"],            // Netgsm DID (E.164). Yeni tenant = yeni DID buraya
    "allowed_addresses": ["NETGSM_SIP_IP/32"], // §2'de not alınan Netgsm SIP IP'si
    "media_encryption": "SIP_MEDIA_ENCRYPT_DISABLE"
  }
}
```

`infra/livekit/dispatch-rule.json` hazır — değiştirme (çağrıyı `call-` önekli odaya alır,
`telesekreter` agent'ını dispatch eder; DID `sip.trunkPhoneNumber` attribute'unda gelir).

Uygula (`lk` CLI — [kurulum](https://github.com/livekit/livekit-cli)):

```bash
export LIVEKIT_URL=ws://localhost:7880
export LIVEKIT_API_KEY=...        # infra/compose/.env ile aynı
export LIVEKIT_API_SECRET=...
lk sip inbound create infra/livekit/inbound-trunk.json
lk sip dispatch create infra/livekit/dispatch-rule.json

# kontrol
lk sip inbound list
lk sip dispatch list
```

---

## 5. Firewall — yalnız gerekeni aç

| Port | Protokol | Kime açık | Ne |
|------|----------|-----------|-----|
| 80/443 | tcp | herkes | Caddy (backend API + TLS) |
| 5060 | udp+tcp | **yalnız Netgsm SIP IP** | SIP signaling |
| 10000-20000 | udp | **yalnız Netgsm SIP IP** | RTP ses medyası |
| 50000-60000 | udp | herkes* | WebRTC/RTC (LiveKit) |

Kapalı kalacaklar: 5432/3306 (DB — compose zaten `127.0.0.1`'e bağlar), 9000/9001 (MinIO),
6379 (Redis), 7880 (LiveKit API — iç ağ; dışarıdan erişim gerekirse Caddy arkasına al).

Örnek (ufw):

```bash
ufw allow 80,443/tcp
ufw allow from <NETGSM_SIP_IP> to any port 5060 proto udp
ufw allow from <NETGSM_SIP_IP> to any port 5060 proto tcp
ufw allow from <NETGSM_SIP_IP> to any port 10000:20000 proto udp
ufw allow 50000:60000/udp
ufw enable
```

---

## 6. Tenant'ı DID'e bağla (backend onboarding)

Tüm onboarding uçları `X-Internal-Key` ister:

```bash
KEY=<INTERNAL_API_KEY>
BASE=https://<domain-veya-ip>

# 1) Tenant + DID
curl -sX POST $BASE/api/tenants -H "X-Internal-Key: $KEY" -H "Content-Type: application/json" -d '{
  "businessName": "Örnek Kuaför",
  "did": "0850XXXXXXX",
  "ownerPhone": "+905551112233",
  "extraPrompt": "Pazar günleri kapalıyız."
}'
# yanıt: { "tenantId": "...", "did": "...", "forwardingInstruction": "**21*0850XXXXXXX#" }

TID=<yanıttaki tenantId>

# 2) Hizmetler
curl -sX POST $BASE/api/tenants/$TID/services -H "X-Internal-Key: $KEY" \
  -H "Content-Type: application/json" -d '{ "name": "Saç Kesimi", "durationMinutes": 30 }'

# 3) Çalışma saatleri (0=Pazar ... 6=Cumartesi)
curl -sX PUT $BASE/api/tenants/$TID/hours -H "X-Internal-Key: $KEY" \
  -H "Content-Type: application/json" -d '[
    { "day": 1, "open": "09:00", "close": "19:00", "isClosed": false },
    { "day": 2, "open": "09:00", "close": "19:00", "isClosed": false },
    { "day": 0, "open": "00:00", "close": "00:00", "isClosed": true }
  ]'

# 4) (Opsiyonel) CRM konuşma şablonu — {business_name}/{services}/{business_hours} yer tutucuları
curl -sX PUT $BASE/api/tenants/$TID -H "X-Internal-Key: $KEY" \
  -H "Content-Type: application/json" -d '{
    "businessName": "Örnek Kuaför",
    "promptTemplate": "Sen {business_name} telesekreterisin. Hizmetler: {services}. Saatler: {business_hours}."
  }'
```

**İşletmenin mevcut numarası varsa** (GSM/sabit hat): müşteri numara değiştirmesin diye
koşulsuz yönlendirme kur — işletme telefonundan `**21*0850XXXXXXX#` çevir + arama tuşu
(yanıttaki `forwardingInstruction`). Doğrulama sonrası:

```bash
curl -sX POST $BASE/api/tenants/$TID/numbers/0850XXXXXXX/verify -H "X-Internal-Key: $KEY"
```

Detay + tarife tradeoff'ları: [onboarding-call-forwarding.md](onboarding-call-forwarding.md).

---

## 7. voice-agent ayarları (gecikme dahil)

Compose zaten env'leri geçirir; özelleştirme `infra/compose/.env` (veya native çalıştırıyorsan
`voice-agent/.env`):

```dotenv
# Zorunlu
LIVEKIT_URL=ws://livekit:7880          # compose içi; native ise ws://<vps>:7880
LIVEKIT_API_KEY=...
LIVEKIT_API_SECRET=...
BACKEND_BASE_URL=http://backend:8080   # compose içi
INTERNAL_API_KEY=<backend ile aynı>

# Sağlayıcılar (self-host varsayılanları)
STT_PROVIDER=whisper                   # whisper | deepgram | azure | openai
LLM_PROVIDER=local                     # local | openai
TTS_PROVIDER=piper                     # piper | azure | elevenlabs | openai
SPEECH_LANGUAGE=tr

# Gecikme / diyalog akıcılığı (yeni)
TURN_DETECTION=multilingual            # model tabanlı sıra tespiti; sorun olursa "vad"
MIN_ENDPOINTING_DELAY=0.4              # kullanıcı sustuktan sonra en az bekleme (sn)
MAX_ENDPOINTING_DELAY=5.0              # en çok bekleme (sn)
TOOL_TIMEOUT_SECONDS=4                 # araç çağrısı timeout — ölü hava sınırı

# Çağrı kaydı (opsiyonel; egress CPU-only VPS'te ağır — pilotta aç)
RECORDING_ENABLED=false
```

> DID eşleşmezse: LiveKit SIP DID'i `sip.trunkPhoneNumber` attribute'unda iletir.
> Netgsm farklı başlık kullanırsa `voice-agent/did.py` → `_DID_ATTRIBUTE_KEYS`'e tek satır ekle,
> `docker compose up -d --build voice-agent`.

---

## 8. Yol B — Netsantral hibrit (opsiyonel karar katmanı)

Yol A çalıştıktan sonra eklenebilir. Fark: çağrı önce backend webhook'una düşer
(tenant açık mı? → yönlendir / TTS mesajı okut), ses yine SIP trunk'tan akar.

1. Backend `.env`:
   ```dotenv
   NETSANTRAL_WEBHOOK_TOKEN=uzun-rastgele-sir
   NETSANTRAL_AGENT_DID=0850YYYYYYY     # SIP trunk'a bağlı İÇ numara (tenant DID'i DEĞİL)
   ```
2. Netsantral panel → **Custom (Özel) API fonksiyonu**:
   - URL: `https://<backend>/netsantral/inbound`, metod: JSON POST
   - Statik değişken: `token = <NETSANTRAL_WEBHOOK_TOKEN>`
3. Trunk `numbers` listesine `NETSANTRAL_AGENT_DID`'i koy (tenant DID'leri artık webhook'ta çözülür).
4. LiveKit'siz doğrulama:
   ```bash
   curl -sX POST https://<backend>/netsantral/inbound \
     -H "Content-Type: application/json" \
     -d '{"token":"<TOKEN>","aranan_no":"<tenant-DID>","arayan_no":"05551112233","arama_id":"t1"}'
   # açık tenant → {"status":"success","result":"dynamic","redirect":"<NETSANTRAL_AGENT_DID>",...}
   # bilinmeyen numara → {"result":"1","data":"<TTS metni>"} ; yanlış token → 401
   ```

Detay: [netsantral-setup.md](netsantral-setup.md).

---

## 9. SMS + giden arama (hat gelince)

- **SMS (hatırlatma):** msgheader onayı gelince backend `.env`:
  `SMS_PROVIDER=netgsm`, `NETGSM_USERCODE`, `NETGSM_PASSWORD`, `NETGSM_MSGHEADER`.
  Randevu (T-24s) + geciken ödeme hatırlatmaları otomatik ([reminders.md](reminders.md)).
- **Giden kampanya araması:** Netgsm outbound trunk → LiveKit outbound trunk tanımla;
  `OUTBOUND_DIALER=livekit` + `NETGSM_SIP_OUTBOUND_TRUNK_ID`. İYS onayı + izinli saat
  (09:00–21:00) kapısı hazır ([compliance-iys.md](compliance-iys.md)).

---

## 10. Uçtan uca doğrulama sırası

| # | Test | Beklenen |
|---|------|----------|
| 1 | `curl $BASE/health` | `{"status":"ok","db":"postgres",...}` |
| 2 | `python scripts/smoke_test.py` (BASE_URL + INTERNAL_API_KEY ile) | 17/17 PASS |
| 3 | `lk sip inbound list` / `dispatch list` | trunk + kural görünür |
| 4 | **DID'i gerçek telefonla ara** | KVKK anonsu çalar, iki yönlü ses |
| 5 | Diyalogda randevu al | `appointments` kaydı; aynı slot ikinci deneme → "az önce doldu" |
| 6 | Görüşme sonrası | WhatsApp/console bilgilendirme + `call_logs` satırı + transkript |
| 7 | `docker compose logs -f voice-agent` | `Çağrı: DID=... tenant=...` logu |

Spike listesi (gecikme/kalite ölçümü dahil): [spikes.md](spikes.md).

---

## 11. Sorun giderme

| Belirti | Muhtemel neden | Çözüm |
|---------|----------------|-------|
| Çağrı hiç düşmüyor | Netgsm VPS IP'sini whitelist'lememiş / 5060 kapalı | Netgsm destek + firewall kuralı (§5) |
| Çağrı düşüyor, ses yok / tek yönlü | RTP portları (10000-20000/udp) kapalı veya NAT | Firewall + `sip.yaml`'da public IP ayarı |
| "SIP 403/  trunk not found" | `allowed_addresses` yanlış IP veya `numbers` DID formatı | Netgsm SIP IP'sini teyit et; DID'i `+90850...` E.164 dene |
| Agent cevap vermiyor (oda boş) | dispatch rule yok / agent adı uyuşmuyor | `lk sip dispatch list`; agent adı `telesekreter` olmalı |
| "Üzgünüm, bu numara tanımlı değil" | DID tenant'a kayıtlı değil / format farkı | §6 onboarding; `GET /internal/tenants/by-did/{did}` ile dene |
| Ajan geç cevap veriyor | turn detection modeli inmemiş / STT yavaş | voice-agent loglarına bak; `TURN_DETECTION=vad` ile karşılaştır; Whisper model boyutunu küçült (`WHISPER_MODEL=small`) |
| Backend testleri lokalde 401 | `backend/.env` test config'ini eziyor | Testten önce `.env`'i geçici taşı (bilinen davranış) |
| `dotnet ef migrations add` SQLite tipi üretiyor | `backend/.env` `DB_PROVIDER=sqlite` env'i eziyor | Migration üretirken `.env`'i geçici taşı + `DB_PROVIDER=postgres` |

---

## 12. Güvenlik + KVKK kontrol listesi

- [ ] Tüm sırlar yalnız `.env`'de (Netgsm şifresi, LiveKit secret, `INTERNAL_API_KEY`) — commit yok.
- [ ] 5060 + RTP yalnız Netgsm IP'sine açık; DB/MinIO/Redis dışa kapalı.
- [ ] Backend yalnız Caddy (TLS) arkasında; tenant/campaign uçları `X-Internal-Key` korumalı.
- [ ] KVKK kayıt anonsu her çağrıda çalıyor (`RECORDING_NOTICE`, atlanamaz).
- [ ] Saklama/imha aktif: `RETENTION_ENABLED=true` (varsayılan) + MinIO ILM kuralı
      (`RECORDING_RETENTION_DAYS` = backend `RETENTION_RECORDING_DAYS`). Politika:
      [legal/saklama-imha-politikasi.md](legal/saklama-imha-politikasi.md).
- [ ] DSAR talepleri için uç hazır: `POST /api/tenants/{id}/dsar/erase {"phone":"+90..."}`.
- [ ] Aydınlatma metinleri yayında ([legal/](legal/README.md)) — yer tutucular doldurulmuş.
