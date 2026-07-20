# NetSIP Bağlama Talimatnamesi (SOP)

Netgsm SIP hattını canlıya alma **operasyonel sıra**sı. Her adımın bir **çıkış kapısı (gate)**
vardır — kapı geçilmeden sonraki adıma geçme. Her maddenin altında **nasıl yapılacağı** var;
daha derin teknik açıklama için [netgsm-sip-gecis-rehberi.md](netgsm-sip-gecis-rehberi.md).

## Ortam Değişkenlerini Tanımla

Kabuğa bir kez yapıştır — tüm komutlarda otomatik dönüşür:

```bash
export VPS_IP="<VPS_PUBLIC_IP>"                      # curl -4 https://api.ipify.org
export DOMAIN="api.firma.com"                        # (opsiyonel; IP'ye doğrudan)
export BASE="https://$DOMAIN"                        # veya "https://$VPS_IP"
export KEY="<INTERNAL_API_KEY>"                      # infra/compose/.env
export DID="0850XXXXXXX"                             # Netgsm DID
export NETGSM_SIP_IP="<NETGSM_SIP_SUNUCU_IP>"       # Netgsm ticket'ından
export TID="<TENANT_ID>"                             # İlk tenant kurulumunda alınan ID
```

---

## Bölüm 1 — SIP'e DOKUNMADAN ÖNCE (ön hazırlık)

Bunlar bitmeden Netgsm/SIP adımına geçme. Çoğu onay süresi uzun; en başta başlat.

### 1.1 Tedarik (paralel başlat — onaylar günler sürer)

- [ ] **VPS hazır**: public statik IP, Docker + Compose kurulu, repo klonlu.
  - Nasıl: VPS sağlayıcıda Ubuntu 22.04+ al, "static/dedicated IP" seçili olsun. SSH'la:
    ```bash
    curl -fsSL https://get.docker.com | sh          # Docker + Compose plugin
    docker --version && docker compose version       # doğrula
    git clone <repo-url> ai-telesekreter && cd ai-telesekreter
    ```

- [ ] **Netgsm kurumsal başvuru** onaylı.
  - Nasıl: netgsm.com.tr → kurumsal hesap. Şahıs şirketiysen vergi levhası + imza sirküleri yükle.
    SIP Trunk kurumsal onay ister; bireysel hesapta çıkmaz.

- [ ] **Netgsm SIP Trunk + DID (0850…)** talebi açık.
  - Nasıl: Netgsm müşteri temsilcisi / destek ticket → "SIP Trunk + 0850 DID numarası istiyorum".

- [ ] **IP auth** iste → **VPS public IP'sini whitelist ettir**.
  - Nasıl: önce VPS IP'sini öğren:
    ```bash
    curl -4 https://api.ipify.org        # çıkan IPv4 = whitelist edilecek IP
    ```
    Netgsm'e ticket/mail at (self-servis panelde yok, ekip tanımlar):
    > SIP Trunk için IP tabanlı kimlik doğrulama (IP auth) istiyorum.
    > Statik public IP: `<VPS_IP>` — bu IP'yi trunk'a yetkili tanımlayın.
    > Gelen SIP çağrılarını gönderdiğiniz **Netgsm SIP sunucu IP'sini** bana bildirin.
    > Kodek: G.711 (PCMU/PCMA). DID: `<0850…>`
  - IP auth vermezlerse user/pass (SIP register) alırsın → §2.2'de `auth_username/auth_password`.

- [ ] **SMS msgheader** talebini de şimdi başlat (onayı en uzun süren; §3 için lazım).
  - Nasıl: Netgsm panel → SMS → Gönderici Adı (başlık) başvurusu. Marka/işletme adı gir; onay
    marka tescili/evrak ister, günler sürer — o yüzden en başta.

- [ ] Netgsm'den **SIP sunucu IP'si** + **DID** yazılı alındı.
  - Nasıl: yukarıdaki ticket yanıtında ikisi de yazılı gelmeli. Bir yere kaydet — 2.1 ve 2.2'de lazım.

- [ ] (Önerilen) **Domain** → `api.firma.com` DNS A kaydı VPS IP'sine.
  - Nasıl: alan adı DNS panelinde `A  api  <VPS_IP>`. Caddy otomatik Let's Encrypt TLS alır.
    Domain'siz de çalışır (self-signed) ama sertifika uyarısı olur.

> **Gate 1.1:** Netgsm SIP IP'si + DID elde + VPS IP whitelist onayı yazılı. Yoksa dur.

### 1.2 Yerel/dry-run doğrulama (hat gelmeden sistem sağlam mı?)

- [ ] `infra/compose/.env` dolduruldu. **Commit yok.**
  - Nasıl:
    ```bash
    cd infra/compose && cp .env.example .env
    docker run --rm livekit/livekit-server generate-keys   # LIVEKIT_API_KEY/SECRET üret, .env'e koy
    ```
    `.env`'de doldur: `POSTGRES_PASSWORD` (güçlü rastgele), `INTERNAL_API_KEY` (uzun rastgele),
    `LIVEKIT_API_KEY`/`LIVEKIT_API_SECRET`. Rastgele üretmek için: `openssl rand -hex 32`.

- [ ] Stack ayağa kalktı: hepsi healthy/running.
  - Nasıl:
    ```bash
    docker compose up -d --build
    docker compose ps      # postgres "healthy"; backend/voice-agent/livekit/livekit-sip "running"
    ```
    Bir servis düşükse: `docker compose logs -f <servis>`.

- [ ] `curl $BASE/health` → `{"status":"ok",...}`.
  - Nasıl: `curl -s $BASE/health` (domain yoksa `curl -sk https://<VPS_IP>/health`).

- [ ] `python scripts/smoke_test.py` → **17/17 PASS**.
  - Nasıl:
    ```bash
    BASE_URL=$BASE INTERNAL_API_KEY=$KEY python scripts/smoke_test.py
    ```
    401 alırsan `backend/.env` test config'ini eziyordur → testten önce geçici taşı
    (`mv backend/.env backend/.env.bak`), sonra geri koy.

> **Gate 1.2:** Health OK + smoke 17/17. Sistem SIP olmadan sağlam. Yoksa SIP'e geçme —
> önce burada çöz.

### 1.3 Tenant + içerik hazır

- [ ] Tenant + DID kaydı → `forwardingInstruction` not alındı.
  - Nasıl:
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
  - Nasıl:
    ```bash
    curl -sX POST $BASE/api/tenants/$TID/services -H "X-Internal-Key: $KEY" \
      -H "Content-Type: application/json" -d '{ "name": "Saç Kesimi", "durationMinutes": 30 }'

    curl -sX PUT $BASE/api/tenants/$TID/hours -H "X-Internal-Key: $KEY" \
      -H "Content-Type: application/json" -d '[
        { "day": 1, "open": "09:00", "close": "19:00", "isClosed": false },
        { "day": 0, "open": "00:00", "close": "00:00", "isClosed": true }
      ]'   # 0=Pazar … 6=Cumartesi
    ```

- [ ] (Varsa) konuşma şablonu ayarlandı.
  - Nasıl: `PUT $BASE/api/tenants/$TID` içinde `promptTemplate` — yer tutucular:
    `{business_name}` / `{services}` / `{business_hours}`.

- [ ] KVKK aydınlatma metinleri yer tutucular doldurulmuş, yayında.
  - Nasıl: `docs/legal/` altındaki metinlerde `<...>` yer tutucularını işletme bilgisiyle doldur.
    Kayıt anonsu (`RECORDING_NOTICE`) her çağrıda otomatik çalar, atlanamaz.

> **Gate 1.3:** `curl -s $BASE/internal/tenants/by-did/0850XXXXXXX -H "X-Internal-Key: $KEY"`
> tenant'ı döndürüyor. DID sistemde tanımlı.

---

## Bölüm 2 — SIP BAĞLAMA (sıra kritik)

Sıra: **önce ağ/güvenlik → sonra LiveKit trunk → en son gerçek arama testi.**

### 2.1 Firewall (aramadan ÖNCE)

- [ ] Portları aç — SIP/RTP yalnız Netgsm IP'sine.
  - Nasıl (ufw; `<NETGSM_SIP_IP>` = 1.1'de alınan IP):
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

> **Gate 2.1:** `ufw status` kuralları yukarıdaki tabloyla birebir. Fazla açık port yok.

### 2.2 LiveKit SIP trunk + dispatch

- [ ] `infra/livekit/inbound-trunk.json` düzenle.
  - Nasıl: `numbers` = DID E.164 (`+90850XXXXXXX`), `allowed_addresses` = `<NETGSM_SIP_IP>/32`.
    IP auth yoksa `auth_username`/`auth_password` ekle. `media_encryption` değişmez.

- [ ] Trunk + dispatch uygula.
  - Nasıl (`lk` CLI kurulu olmalı — github.com/livekit/livekit-cli):
    ```bash
    export LIVEKIT_URL=ws://localhost:7880
    export LIVEKIT_API_KEY=...        # infra/compose/.env ile AYNI
    export LIVEKIT_API_SECRET=...
    lk sip inbound create infra/livekit/inbound-trunk.json
    lk sip dispatch create infra/livekit/dispatch-rule.json   # dosya hazır, değiştirme
    ```

- [ ] Trunk + kural görünür.
  - Nasıl: `lk sip inbound list` ve `lk sip dispatch list`. Dispatch agent adı **`telesekreter`**.

> **Gate 2.2:** Trunk + dispatch listede, agent adı `telesekreter`.

### 2.3 Gerçek arama — uçtan uca

- [ ] DID'i **gerçek telefonla** ara → KVKK anonsu + **çift yönlü ses**.
  - Nasıl: cepten `0850XXXXXXX` ara. Anons çalmıyorsa → 2.1 firewall/whitelist. Ses tek yönlüyse
    → RTP portları (10000-20000/udp) veya NAT/public IP ayarı (`sip.yaml`).

- [ ] Randevu al → `appointments` kaydı düştü.
  - Nasıl: diyalogda randevu iste; DB'de kontrol:
    ```bash
    docker compose exec postgres psql -U postgres -d telesekreter -c \
      "select id, starts_at, customer_phone from appointments order by created_at desc limit 3;"
    ```

- [ ] Aynı slotu ikinci kez dene → "az önce doldu" (çakışma koruması).
- [ ] Görüşme sonrası → WhatsApp/console bilgilendirme + `call_logs` satırı + transkript.
- [ ] Log akışı doğru.
  - Nasıl: `docker compose logs -f voice-agent` → `Çağrı: DID=… tenant=…` satırı.

> **Gate 2.3:** Yukarıdakilerin hepsi ✔ = **Yol A canlı**.

---

## Bölüm 3 — SONRASI (opsiyonel, sırayla)

- [ ] **İşletme mevcut numarasını taşıyorsa:** koşulsuz yönlendirme kur.
  - Nasıl: işletme telefonundan `**21*0850XXXXXXX#` çevir + arama tuşu (tenant yanıtındaki
    `forwardingInstruction`). Sonra doğrula:
    ```bash
    curl -sX POST $BASE/api/tenants/$TID/numbers/0850XXXXXXX/verify -H "X-Internal-Key: $KEY"
    ```
    Tarife tradeoff'ları: [onboarding-call-forwarding.md](onboarding-call-forwarding.md).

- [ ] **SMS hatırlatma:** msgheader onayı gelince aç.
  - Nasıl: backend `.env` → `SMS_PROVIDER=netgsm`, `NETGSM_USERCODE`, `NETGSM_PASSWORD`,
    `NETGSM_MSGHEADER`. Randevu (T-24s) + geciken ödeme hatırlatmaları otomatik
    ([reminders.md](reminders.md)).

- [ ] **Netsantral hibrit (Yol B):** karar katmanı istenirse — rehber §8.
- [ ] **Giden arama:** İYS onayı + izinli saat (09:00–21:00) kapısı — rehber §9 +
  [compliance-iys.md](compliance-iys.md).

---

## Hızlı sorun-giderme (ilk bakılacaklar)

| Belirti | İlk kontrol |
|---------|-------------|
| Çağrı hiç düşmüyor | Netgsm IP whitelist + 5060 açık mı (Gate 2.1) |
| Ses tek yönlü / yok | 10000-20000/udp açık mı + `sip.yaml` public IP |
| Agent sessiz (oda boş) | `lk sip dispatch list`, agent adı `telesekreter` |
| "Numara tanımlı değil" | `GET /internal/tenants/by-did/{did}` (Gate 1.3) |
| Backend testleri 401 | `backend/.env`'i testten önce geçici taşı |
| Ajan geç cevap | `docker compose logs voice-agent`; `TURN_DETECTION=vad` dene, `WHISPER_MODEL=small` |

Tam tablo: rehber §11.

---

## Altın kural

**Ön hazırlık (Bölüm 1) bitmeden SIP'e (Bölüm 2) dokunma.** Sistemi önce dry-run'da sağlamla
(Gate 1.2), sonra hattı bağla. Hat sorunlarının %90'ı atlanan bir gate'tir — sırayı bozma.
