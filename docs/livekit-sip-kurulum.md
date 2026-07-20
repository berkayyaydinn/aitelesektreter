# LiveKit SIP Trunk + Dispatch Kurulum (Bölüm 5 & 6)

Netgsm SIP'i AI telesekreter'e LiveKit üzerinden bağlama. `$VPS_IP`, `$DOMAIN`, `$NETGSM_SIP_IP`,
`$DID` yer tutucuları kendi değerlerini ile doldur.

## Ortam Değişkenleri Tanımla (tüm komutlarda kullan)

```bash
# Kendi değerlerini gir
export VPS_IP="<VPS_PUBLIC_IP>"                      # curl -4 https://api.ipify.org çıktısı
export DOMAIN="api.firma.com"                        # (opsiyonel; IP'ye doğrudan ping de olur)
export BASE_URL="https://$DOMAIN"                    # veya "https://$VPS_IP" (self-signed TLS)
export NETGSM_SIP_IP="<SIP_SUNUCU_IP>"               # Netgsm ticket yanıtında yazıyor
export DID="0850XXXXXXX"                             # Netgsm'den aldığın 0850 numarası (E.164: +90850...)
export DID_E164="+90${DID:1}"                        # +90850... formatı

# LiveKit config (infra/compose/.env ile AYNI olmalı)
export LIVEKIT_URL="ws://localhost:7880"
export LIVEKIT_API_KEY="<infra/compose/.env'den LIVEKIT_API_KEY>"
export LIVEKIT_API_SECRET="<infra/compose/.env'den LIVEKIT_API_SECRET>"

# İç API (Bölüm 1'de alınan key)
export INTERNAL_API_KEY="<INTERNAL_API_KEY>"
```

Kabuğa bir kez yapıştır — tüm komutlarda `$DOMAIN`, `$VPS_IP` vb. otomatik değişecek.

---

## Bölüm 5 — LiveKit SIP Trunk Kurma

### 5.1 `infra/livekit/inbound-trunk.json` Düzenle

```bash
cat > infra/livekit/inbound-trunk.json <<'EOF'
{
  "outbound_number": "",
  "inbound_number": "$DID_E164",
  "inbound_number_regex": "",
  "inbound_addresses": [],
  "inbound_addresses_regex": ".*",
  "outbound_address": "$NETGSM_SIP_IP",
  "outbound_address_regex": "",
  "inbound_number_map": {},
  "outbound_number_map": {},
  "allowed_addresses": ["$NETGSM_SIP_IP/32"],
  "allowed_numbers": [],
  "allow_all_numbers": true,
  "auth_username": "",
  "auth_password": "",
  "media_encryption": "preferred"
}
EOF
```

**Önemli:** Eğer Netgsm **IP auth vermedi, user/pass verdiyse**, üsteki değişkenleri doldur:
```bash
# IP auth yoksa:
sed -i "s|\"auth_username\": \"\"|\"auth_username\": \"<NETGSM_USERNAME>\"|" infra/livekit/inbound-trunk.json
sed -i "s|\"auth_password\": \"\"|\"auth_password\": \"<NETGSM_PASSWORD>\"|" infra/livekit/inbound-trunk.json
```

### 5.2 Dispatch Kuralı (`infra/livekit/dispatch-rule.json`)

Zaten hazır, **değiştirme**. İçinde `"agent_name": "telesekreter"` var, bu sistem tarafından aranıyor.

Doğrulamak istersen:
```bash
cat infra/livekit/dispatch-rule.json
```

Çıktı: agent_name = `telesekreter`.

### 5.3 LiveKit CLI Kur

VPS'te (şu an `ai-telesekreter` dizinindeysen):

```bash
# livekit-cli indir (macOS/Linux)
curl -L https://github.com/livekit/livekit-cli/releases/download/v0.3.15/livekit-cli-linux-amd64 -o lk
chmod +x lk
sudo mv lk /usr/local/bin/

# Doğrula
lk --version
```

Windows'taysanz: GitHub → Releases → `livekit-cli-windows-amd64.zip`, extract, PATH'e ekle.

### 5.4 Trunk + Dispatch Oluştur

```bash
# Trunk oluştur
lk sip inbound create infra/livekit/inbound-trunk.json

# Dispatch kural ekle
lk sip dispatch create infra/livekit/dispatch-rule.json
```

**Hata alırsan**, `docker compose logs -f livekit` kontrol et — port 7880 accessible mi?
```bash
curl -s ws://localhost:7880 | head -5   # WebSocket accessible mi test
```

### 5.5 Doğrula — Trunk + Dispatch Görünürler

```bash
lk sip inbound list     # inbound-trunk görünüyor mu?
lk sip dispatch list    # dispatch-rule görünüyor, agent=telesekreter?
```

Çıktı:
```
Trunks:
  inbound-trunk
    Numbers: +90850XXXXXXX
    Allowed Addresses: <NETGSM_SIP_IP>/32

Dispatch Rules:
  dispatch-rule
    Agent: telesekreter
```

> **Gate 5:** Trunk + dispatch listede, agent adı `telesekreter`. Yoksa dur.

---

## Bölüm 6 — Gerçek Arama Testi (Uçtan Uca)

### 6.1 Health + Smoke Kontrol (Tekrar)

```bash
# Backend hazır mı?
curl -s $BASE_URL/health
# Yanıt: {"status":"ok","timestamp":"..."}

# Sistem testleri hepsi pass?
BASE_URL=$BASE_URL INTERNAL_API_KEY=$INTERNAL_API_KEY python scripts/smoke_test.py
# Yanıt: 17/17 PASS
```

### 6.2 Tenant Doğrula

Bölüm 3'te tenant kaydı yaptıyssan, DID sistemde tanımlı mı kontrol:

```bash
curl -s "$BASE_URL/internal/tenants/by-did/$DID" \
  -H "X-Internal-Key: $INTERNAL_API_KEY" | jq .
```

Yanıt: tenant nesnesi (id, businessName, did, vb). Yoksa Gate 3 geçilmemiş — tenant kaydını tekrar yap.

### 6.3 KVKK Anonsu Hazır mı?

Kayıt anonsu (`RECORDING_NOTICE`) her çağrıda otomatik çalmalı. `docs/legal/` altındaki metinler (KVKK
aydınlatması, kayıt anonsu) doldurulmuş ve yayında mı kontrol:

```bash
ls -la docs/legal/
```

Dosyalar orada ve boş değilse hazır.

### 6.4 Gerçek Arama — DID'i Ara

**Farklı bir telefondan** (cepte başka numara, işletme telefonundan, arkadaşın cep vb.) DID'i ara:

```
0850XXXXXXX ara (veya E.164: +90850XXXXXXX)
```

**Beklenen:**
1. Çoğunlukla sessizlik (ses ortamı hazırlanıyor)
2. **KVKK kayıt anonsu çalar** — "Aramanız kaydedilecektir..." (atlanamaz)
3. AI selamlar (konuşma başlıyor — 2-5 saniye gecikme normal, model yükleniyor)
4. Soru: "Merhaba, kaçıncı gün randevu istiyorsunuz?"
5. Siz cevap verirsiniz ("Pazartesi", "2 gün sonra", vb.)
6. AI tarafından dan: "Hangi hizmeti istiyorsunuz?" (hizmetler otomatik listelenir)
7. Hizmet + saat seçin → "Randevunuz kaydedildi, WhatsApp'ta bilgi alacaksınız"
8. Çağrı kapanır.

**Problemler:**
| Belirti | Kontrol |
|---------|---------|
| Çağrı hiç düşmüyor | Netgsm IP whitelist + 5060 açık (Gate 4) |
| "Sayı taşıyor" veya "Hizmet bulunamadı" | `GET /internal/tenants/by-did/$DID` tekrar doğrula |
| Anons çalmıyor | `docs/legal/` metinleri doldu mu? |
| Ses tek yönlü (sadece sen işitiyor) | 10000-20000/udp açık + `sip.yaml` public IP |
| AI cevap vermiyor, sessiz kalıyor | `docker compose logs voice-agent` → "error" var mı |
| Ajan çok geç cevap veriyor (>10s) | `TURN_DETECTION=vad`, `WHISPER_MODEL=small` dene |

### 6.5 DB'de Randevu Kaydı Kontrol

Başarılı randevu sonrası `appointments` tablosuna satır düşüyor mu:

```bash
docker compose exec postgres psql -U postgres -d telesekreter -c \
  "SELECT id, tenant_id, customer_phone, starts_at, created_at \
   FROM appointments \
   ORDER BY created_at DESC LIMIT 5;"
```

Çıktı: son 5 randevu, senin aramanızdan sonra yeni satır.

### 6.6 Çakışma Koruması Test (Opsiyonel)

Aynı slotu **hemen tekrar iste**:
1. DID'i yeniden ara
2. Aynı gün + aynı saati iste
3. AI: "Az önce doldu, başka saat?" (çakışma koruması çalışıyor, ok)

### 6.7 Call Log + Transkript

Çağrı sonrası `call_logs` tablosunda kayıt + transkript var mı:

```bash
docker compose exec postgres psql -U postgres -d telesekreter -c \
  "SELECT id, tenant_id, did, caller_phone, status, transcript \
   FROM call_logs \
   ORDER BY created_at DESC LIMIT 1;"
```

Transkript burada (JSON), WhatsApp/SMS bildiriminde kısıtlı versiyon gider.

### 6.8 Log Stream Kontrol

Gerçek zamanlı agentın ne yaptığını görmek:

```bash
docker compose logs -f voice-agent
```

Ara yapıldığında satırlar:
```
voice-agent | Çağrı başladı: DID=0850XXXXXXX tenant=<TID>
voice-agent | Transcript: "Merhaba", "Pazartesi"
voice-agent | Randevu kaydedildi: slot=<ID>
```

Ctrl+C ile çıkış.

> **Gate 6:** Yukarıdakilerin hepsi ✔ = **Inbound canlı**. Gelen çağrılar otomatik AI cevaplayıyor.

---

## Sonrası (İsteğe Bağlı)

### Gelen Çağrıları Kendi Numaraya Yönlendir

İşletme mevcut telefonundan al: dış numaraya yönlendirme kur, DID'e geçenler kendi telefonuna gitsin:

```bash
# İşletme telefonundan USSD kodu çevir (Netgsm tier'a bağlı)
**21*0850XXXXXXX#  → arama tuşu
```

Doğrula:
```bash
curl -sX POST "$BASE_URL/api/tenants/$TID/numbers/$DID/verify" \
  -H "X-Internal-Key: $INTERNAL_API_KEY"
```

### SMS Hatırlatması (Msgheader Onayı Gelince)

Msgheader başvurusu onaylandığında (Bölüm 1 uzun adımlardan biri):

```bash
# backend/.env'e ekle:
# SMS_PROVIDER=netgsm
# NETGSM_USERCODE=<kullanıcı_kodu>
# NETGSM_PASSWORD=<netgsm_api_şifresi>
# NETGSM_MSGHEADER=<onaylanmış_başlık>

docker compose restart backend
```

Randevu T-24s'de SMS hatırlatması otomatik gitmeye başlar.

---

## Hızlı Checklist

- [ ] `.env` doldurulmuş, stack running
- [ ] Health + smoke test ✔
- [ ] Tenant kaydı ✔, DID sistemde
- [ ] Firewall açık (5060, 10000-20000/udp)
- [ ] LiveKit trunk + dispatch kurulu
- [ ] Gerçek çağrı → KVKK anonsu + AI cevap ✔
- [ ] Randevu DB'de ✔
- [ ] Transkript kaydedildi ✔
- [ ] Inbound canlı 🎉
