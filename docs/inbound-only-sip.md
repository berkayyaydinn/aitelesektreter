# Inbound-Only SIP Kılavuzu (Sadece Gelen Çağrı)

Netgsm SIP hattını **yalnız gelen çağrı** için canlıya alma sırası. Giden pazarlama (SMS/arama)
**yok** → **İYS / SMS msgheader / MERSİS gerekmez**. Tam SOP (inbound + outbound + hibrit):
[netsip-baglama-talimatnamesi.md](netsip-baglama-talimatnamesi.md).

> **Kapsam:** yalnız inbound. Sonradan giden SMS/arama eklersen İYS + msgheader devreye girer →
> o zaman tam SOP **Bölüm 3**'e ve [compliance-iys.md](compliance-iys.md)'ye dön.

## Ortam Değişkenlerini Tanımla

Kabuğa bir kez yapıştır — tüm komutlarda otomatik dönüşür:

```bash
export VPS_IP="<VPS_PUBLIC_IP>"                      # curl -4 https://api.ipify.org
export DOMAIN="api.firma.com"                        # (opsiyonel; IP'ye doğrudan)
export BASE="https://$DOMAIN"                        # veya "https://$VPS_IP"
export KEY="<INTERNAL_API_KEY>"                      # infra/compose/.env
export DID="0850XXXXXXX"                             # Netgsm DID
export NETGSM_SIP_IP="<NETGSM_SIP_SUNUCU_IP>"       # Netgsm ticket'ından
```

Her adımın bir **çıkış kapısı (gate)** vardır — kapı geçilmeden sonraki adıma geçme.

---

## Bölüm 1 — Tedarik (SIP'e dokunmadan önce)

Onaylar günler sürer; en başta paralel başlat.

- [ ] **VPS hazır**: public statik IP, Docker + Compose kurulu, repo klonlu.
  ```bash
  curl -fsSL https://get.docker.com | sh
  docker --version && docker compose version
  git clone <repo-url> ai-telesekreter && cd ai-telesekreter
  ```

- [ ] **Netgsm kurumsal başvuru** onaylı.
  - netgsm.com.tr → kurumsal hesap. Şahıs şirketiysen vergi levhası + imza sirküleri yükle.
    SIP Trunk kurumsal onay ister; bireysel hesapta çıkmaz.

- [ ] **Netgsm SIP Trunk + DID (0850…)** talebi açık.
  - Netgsm destek ticket → "SIP Trunk + 0850 DID numarası istiyorum".

- [ ] **IP auth** iste → **VPS public IP'sini whitelist ettir**.
  ```bash
  curl -4 https://api.ipify.org        # çıkan IPv4 = whitelist edilecek IP
  ```
  Netgsm'e ticket at:
  > SIP Trunk için IP tabanlı kimlik doğrulama (IP auth) istiyorum.
  > Statik public IP: `<VPS_IP>` — bu IP'yi trunk'a yetkili tanımlayın.
  > Gelen SIP çağrılarını gönderdiğiniz **Netgsm SIP sunucu IP'sini** bana bildirin.
  > Kodek: G.711 (PCMU/PCMA). DID: `<0850…>`
  - IP auth vermezlerse user/pass (SIP register) alırsın → §5'te `auth_username/auth_password`.

- [ ] Netgsm'den **SIP sunucu IP'si** + **DID** yazılı alındı. Kaydet — §4 ve §5'te lazım.

- [ ] (Önerilen) **Domain** → `api.firma.com` DNS A kaydı VPS IP'sine.
  - `A  api  <VPS_IP>`. Caddy otomatik Let's Encrypt TLS alır. Domain'siz de çalışır (self-signed).

> **Not:** Outbound olmadığından **SMS msgheader başvurusu gereksiz** — atla.

> **Gate 1:** Netgsm SIP IP + DID elde + VPS IP whitelist onayı yazılı. Yoksa dur.

---

## Bölüm 2 — Dry-run doğrulama (hat gelmeden sistem sağlam mı?)

- [ ] `infra/compose/.env` dolduruldu. **Commit yok.**
  ```bash
  cd infra/compose && cp .env.example .env
  docker run --rm livekit/livekit-server generate-keys   # LIVEKIT_API_KEY/SECRET üret
  ```
  `.env`'de doldur: `POSTGRES_PASSWORD`, `INTERNAL_API_KEY`, `LIVEKIT_API_KEY`/`LIVEKIT_API_SECRET`.
  Rastgele: `openssl rand -hex 32`.

- [ ] Stack ayağa kalktı: hepsi healthy/running.
  ```bash
  docker compose up -d --build
  docker compose ps      # postgres "healthy"; backend/voice-agent/livekit/livekit-sip "running"
  ```
  Servis düşükse: `docker compose logs -f <servis>`.

- [ ] `curl $BASE/health` → `{"status":"ok",...}` (domain yoksa `curl -sk https://<VPS_IP>/health`).

- [ ] `python scripts/smoke_test.py` → **17/17 PASS**.
  ```bash
  BASE_URL=$BASE INTERNAL_API_KEY=$KEY python scripts/smoke_test.py
  ```
  401 alırsan `backend/.env` test config'ini eziyordur → testten önce geçici taşı
  (`mv backend/.env backend/.env.bak`), sonra geri koy.

> **Gate 2:** Health OK + smoke 17/17. Sistem SIP olmadan sağlam. Yoksa SIP'e geçme.

---

## Bölüm 3 — Tenant + DID kaydı

- [ ] Tenant + DID kaydı → `forwardingInstruction` not alındı.
  ```bash
  curl -sX POST $BASE/api/tenants -H "X-Internal-Key: $KEY" -H "Content-Type: application/json" -d '{
    "businessName": "Örnek Kuaför",
    "did": "0850XXXXXXX",
    "ownerPhone": "+905551112233",
    "extraPrompt": "Pazar günleri kapalıyız."
  }'
  # yanıttaki tenantId'yi sakla: TID=<tenantId>
  ```

- [ ] Hizmetler + çalışma saatleri girildi.
  ```bash
  curl -sX POST $BASE/api/tenants/$TID/services -H "X-Internal-Key: $KEY" \
    -H "Content-Type: application/json" -d '{ "name": "Saç Kesimi", "durationMinutes": 30 }'

  curl -sX PUT $BASE/api/tenants/$TID/hours -H "X-Internal-Key: $KEY" \
    -H "Content-Type: application/json" -d '[
      { "day": 1, "open": "09:00", "close": "19:00", "isClosed": false },
      { "day": 0, "open": "00:00", "close": "00:00", "isClosed": true }
    ]'   # 0=Pazar … 6=Cumartesi
  ```

- [ ] (Varsa) konuşma şablonu: `PUT $BASE/api/tenants/$TID` içinde `promptTemplate` —
  yer tutucular: `{business_name}` / `{services}` / `{business_hours}`.

- [ ] KVKK aydınlatma metinleri doldurulmuş, yayında.
  - `docs/legal/` altındaki `<...>` yer tutucularını işletme bilgisiyle doldur.
    Kayıt anonsu (`RECORDING_NOTICE`) her çağrıda otomatik çalar, atlanamaz.

> **Gate 3:** `curl -s $BASE/internal/tenants/by-did/0850XXXXXXX -H "X-Internal-Key: $KEY"`
> tenant'ı döndürüyor. DID sistemde tanımlı.

---

## Bölüm 4 — Firewall (aramadan ÖNCE)

- [ ] Portları aç — SIP/RTP yalnız Netgsm IP'sine (`<NETGSM_SIP_IP>` = §1'de alınan IP).
  ```bash
  ufw allow 80,443/tcp                                          # Caddy + TLS
  ufw allow from <NETGSM_SIP_IP> to any port 5060 proto udp     # SIP signaling
  ufw allow from <NETGSM_SIP_IP> to any port 5060 proto tcp
  ufw allow from <NETGSM_SIP_IP> to any port 10000:20000 proto udp   # RTP ses
  ufw allow 50000:60000/udp                                     # WebRTC
  ufw enable
  ufw status numbered      # doğrula
  ```
  - DB/MinIO/Redis/7880 **açma** — compose zaten `127.0.0.1`'e bağlar, dışa kapalı kalsın.

> **Gate 4:** `ufw status` kuralları yukarıdaki tabloyla birebir. Fazla açık port yok.

---

## Bölüm 5 — LiveKit SIP trunk + dispatch

- [ ] `infra/livekit/inbound-trunk.json` düzenle.
  - `numbers` = DID E.164 (`+90850XXXXXXX`), `allowed_addresses` = `<NETGSM_SIP_IP>/32`.
    IP auth yoksa `auth_username`/`auth_password` ekle. `media_encryption` değişmez.

- [ ] Trunk + dispatch uygula (`lk` CLI kurulu — github.com/livekit/livekit-cli).
  ```bash
  export LIVEKIT_URL=ws://localhost:7880
  export LIVEKIT_API_KEY=...        # infra/compose/.env ile AYNI
  export LIVEKIT_API_SECRET=...
  lk sip inbound create infra/livekit/inbound-trunk.json
  lk sip dispatch create infra/livekit/dispatch-rule.json   # dosya hazır, değiştirme
  ```

- [ ] Trunk + kural görünür.
  - `lk sip inbound list` ve `lk sip dispatch list`. Dispatch agent adı **`telesekreter`**.

> **Gate 5:** Trunk + dispatch listede, agent adı `telesekreter`.

---

## Bölüm 6 — Gerçek arama (uçtan uca)

- [ ] DID'i **gerçek telefonla** ara → KVKK anonsu + **çift yönlü ses**.
  - Cepten `0850XXXXXXX` ara. Anons çalmıyorsa → §4 firewall/whitelist. Ses tek yönlüyse
    → RTP portları (10000-20000/udp) veya NAT/public IP ayarı (`sip.yaml`).

- [ ] Randevu al → `appointments` kaydı düştü.
  ```bash
  docker compose exec postgres psql -U postgres -d telesekreter -c \
    "select id, starts_at, customer_phone from appointments order by created_at desc limit 3;"
  ```

- [ ] Aynı slotu ikinci kez dene → "az önce doldu" (çakışma koruması).
- [ ] Görüşme sonrası → WhatsApp/console bilgilendirme + `call_logs` satırı + transkript.
- [ ] Log akışı doğru.
  ```bash
  docker compose logs -f voice-agent   # "Çağrı: DID=… tenant=…"
  ```

> **Gate 6:** Yukarıdakilerin hepsi ✔ = **inbound canlı**.

---

## Sonrası (opsiyonel)

- **İşletme mevcut numarasını taşıyorsa:** koşulsuz yönlendirme.
  - İşletme telefonundan `**21*0850XXXXXXX#` çevir + arama tuşu (tenant yanıtındaki
    `forwardingInstruction`). Doğrula:
    ```bash
    curl -sX POST $BASE/api/tenants/$TID/numbers/0850XXXXXXX/verify -H "X-Internal-Key: $KEY"
    ```
    Tarife tradeoff'ları: [onboarding-call-forwarding.md](onboarding-call-forwarding.md).

- **Giden SMS/arama eklemek istersen** → İYS + msgheader + izinli saat kapısı gerekir.
  Bu kılavuz kapsamı dışı → tam SOP **Bölüm 3** + [compliance-iys.md](compliance-iys.md).

---

## Hızlı sorun-giderme

| Belirti | İlk kontrol |
|---------|-------------|
| Çağrı hiç düşmüyor | Netgsm IP whitelist + 5060 açık mı (Gate 4) |
| Ses tek yönlü / yok | 10000-20000/udp açık mı + `sip.yaml` public IP |
| Agent sessiz (oda boş) | `lk sip dispatch list`, agent adı `telesekreter` |
| "Numara tanımlı değil" | `GET /internal/tenants/by-did/{did}` (Gate 3) |
| Backend testleri 401 | `backend/.env`'i testten önce geçici taşı |
| Ajan geç cevap | `docker compose logs voice-agent`; `TURN_DETECTION=vad`, `WHISPER_MODEL=small` |

Tam tablo + teknik detay: [netgsm-sip-gecis-rehberi.md](netgsm-sip-gecis-rehberi.md).

---

## Altın kural

**Bölüm 1 (tedarik) + Bölüm 2 (dry-run) bitmeden Bölüm 4-5'e (SIP) dokunma.** Sistemi önce
sağlamla (Gate 2), sonra hattı bağla. Hat sorunlarının %90'ı atlanan bir gate'tir.
