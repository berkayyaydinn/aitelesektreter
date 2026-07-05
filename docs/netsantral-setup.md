# Netsantral (Netgsm Bulut Santral) Kurulumu — Custom API + SIP Trunk

Sistemi **Netsantral Custom (Özel) API** ile çalıştırma rehberi. LLM ayarı için:
[llm-setup.md](llm-setup.md).

## Neden hibrit (Custom API + SIP Trunk)?

Netsantral **Custom API canlı ses TAŞIMAZ**. İstek/yanıt webhook'tur: PBX çağrıda bilgi gönderir,
sen ya **TTS metni okutursun** ya da çağrıyı **bir NUMARAYA yönlendirirsin** (SIP URI/IP değil).
Doğal Türkçe diyalog için gerçek-zamanlı çift yönlü ses gerekir → bunu **SIP Trunk** verir.

Bu yüzden **hibrit** kurarız:

```
Çağrı → Netsantral PBX
  ├─(1) Custom API webhook → backend POST /netsantral/inbound
  │        aranan_no → tenant lookup + açık/kapalı kararı
  │        yanıt: { result:"dynamic", redirect:"<NETSANTRAL_AGENT_DID>" }   (açıksa)
  │        veya  { result:"1", data:"<TTS metni>" }                          (yok/kapalı)
  └─(2) Netsantral çağrıyı NETSANTRAL_AGENT_DID'e taşır
           → SIP Trunk (IP allowlist) → LiveKit SIP → voice-agent
              STT → LLM → TTS  (doğal diyalog, KVKK anonsu burada çalar)
```

- **Custom API = beyin/karar** (hangi tenant, açık mı, nereye yönlendir). Backend'e yeni endpoint.
- **SIP Trunk = ses yolu.** Mevcut LiveKit SIP hattı (voice-agent) değişmeden kullanılır.

> Not: `call_started`/`call_ended` + KVKK rızası **voice-agent** tarafında (SIP bacağı) işlenir.
> Webhook salt-okumadır (DB'ye yazmaz) → çift kayıt olmaz.

---

## 1. Custom API webhook (backend)

Backend'de hazır uç: `POST /netsantral/inbound` (`Endpoints/NetsantralEndpoints.cs`).

**`.env` (backend):**
```env
NETSANTRAL_WEBHOOK_TOKEN=uzun-rastgele-sir   # Netsantral fonksiyonunda statik değişken olarak gönderilecek
NETSANTRAL_AGENT_DID=08509990000             # SIP-trunk'a bağlı iç DID (AI ajanın oturduğu numara)
```

**Netsantral panelinde Custom fonksiyon:**
1. **Ayarlar → Genel Ayarlar → API İstek Ayarları** (veya Custom entegrasyon fonksiyonu).
2. **URL:** `https://<backend-alanadi>/netsantral/inbound`
3. **Veri metodu:** `JSON POST` (form POST da desteklenir).
4. **Statik değişken:** `token = <NETSANTRAL_WEBHOOK_TOKEN>` (yukarıdaki sır).
5. Gönderilen alanlar (Netsantral sağlar): `arayan_no`, `santral_no`, `aranan_no`, `arama_id`,
   `tus_bilgisi`. Backend `aranan_no`'yu tenant DID'ine eşler (format normalizasyonu otomatik:
   `850..`, `0850..`, `90850..` denenir).
6. **Yanıt sözleşmesi** (backend üretir):
   - Açık: `{ "status":"success", "result":"dynamic", "data":"", "redirect":"<NETSANTRAL_AGENT_DID>" }`
   - Yok/kapalı: `{ "status":"success", "result":"1", "data":"<okunacak metin>" }`

**Kimlik doğrulama:** `token` eşleşmezse `401`. (Opsiyonel ek güvenlik: Netsantral IP'lerini
reverse-proxy/firewall'da allowlist et.)

**Tenant DID kaydı:** İşletmenin numarasını tenant'a bağla — `PhoneNumbers.Did = <aranan numara>`
(`/api/tenants` onboarding'i DID ile kaydeder). `NETSANTRAL_AGENT_DID` ayrı bir iç numaradır,
tenant DID'i değildir.

---

## 2. SIP Trunk (ses yolu)

`NETSANTRAL_AGENT_DID`'e gelen (yönlendirilen) çağrı SIP Trunk ile LiveKit'e taşınır.

1. Netgsm/Netsantral'dan **SIP Trunk** hizmetini aç; `NETSANTRAL_AGENT_DID` numarasını bu trunk'a
   bağla (gelen çağrı IP/alan adına yönlensin — **SIP Trunk Yönlendirme**).
2. LiveKit sunucunun public IP'sini Netsantral'da **whitelist** ettir.
3. `infra/livekit/inbound-trunk.json`:
   - `allowed_addresses` → **Netsantral SIP IP**/32
   - `numbers` → `["<NETSANTRAL_AGENT_DID>"]`
4. Trunk + dispatch kuralını uygula (mevcut akış, değişmez):
   ```bash
   export LIVEKIT_URL=ws://localhost:7880 LIVEKIT_API_KEY=... LIVEKIT_API_SECRET=...
   lk sip inbound create infra/livekit/inbound-trunk.json
   lk sip dispatch create infra/livekit/dispatch-rule.json
   ```
5. voice-agent zaten `telesekreter` agent adıyla çalışır. DID, `sip.trunkPhoneNumber` attribute'unda
   gelir (`voice-agent/did.py`). Netsantral farklı bir SIP başlığı kullanırsa `did.py`
   `_DID_ATTRIBUTE_KEYS`'e o anahtar eklenir (tek satır).

> Detaylı LiveKit SIP adımları ve firewall: `infra/livekit/README.md`.

---

## 3. Doğrulama

1. **Webhook (LiveKit'siz):**
   ```bash
   curl -sX POST https://<backend>/netsantral/inbound \
     -H "Content-Type: application/json" \
     -d '{"token":"<NETSANTRAL_WEBHOOK_TOKEN>","aranan_no":"<kayitli-DID>","arayan_no":"05551112233","arama_id":"t1"}'
   # Beklenen (tenant açık): {"status":"success","result":"dynamic","redirect":"<NETSANTRAL_AGENT_DID>",...}
   ```
   - Bilinmeyen `aranan_no` → `result:"1"` + `data` (TTS). Yanlış/eksik `token` → `401`.
2. **Otomatik test:** `cd backend && dotnet test` → `NetsantralEndpointsTests` geçmeli.
   (Not: `backend/.env` varsa testten önce geçici taşı — testler kendi config'ini enjekte eder.)
3. **Uçtan uca (canlı):** Netsantral Custom fonksiyonu + SIP trunk kurulu → gerçek numarayı ara →
   webhook redirect → LiveKit odası → iki yönlü ses + KVKK anonsu (`docs/spikes.md` Spike 1).

---

## 4. Alternatif: Sadece SIP Trunk (Custom API'siz)

En düşük karmaşıklık/maliyet: Custom API'nin erken karar/loglama katmanını istemiyorsan,
tenant DID'ini doğrudan SIP Trunk'a bağla (`inbound-trunk.json` `numbers` = tenant DID) ve
`/netsantral/inbound` webhook'unu hiç kurma. Doğal diyalog yine çalışır; sadece açık/kapalı ön-kararı
ve yönlendirme esnekliği olmaz (bunu voice-agent prompt'u yönetir).

## Güvenlik
- `NETSANTRAL_WEBHOOK_TOKEN`, LiveKit secret, Netgsm şifresi **yalnız `.env`** — asla commit etme.
- SIP portlarını (5060, RTP aralığı) yalnız Netsantral IP'sine aç (bkz. `infra/livekit/README.md`).
