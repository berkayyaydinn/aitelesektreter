# Kişisel Veri Saklama ve İmha Politikası

> **Hukuki sorumluluk reddi:** Bu metin bir taslak/şablondur ve hukuki danışmanlık niteliği
> taşımaz. Saklama süreleri ilgili mevzuata ve işletme ihtiyacına göre bir avukatla teyit
> edilmelidir. `[...]` alanları doldurulmalıdır.

Bu politika, KVKK ve Kişisel Verilerin Silinmesi, Yok Edilmesi veya Anonim Hâle Getirilmesi
Hakkında Yönetmelik uyarınca verilerin saklanması ve imhasını düzenler.

## 1. Kapsam

AI Telesekreter / VoiceReception altyapısında işlenen tüm kişisel veriler (bkz. veri modeli,
`docs/architecture.md`).

## 2. Saklama Süreleri (Uygulanan Varsayılanlar — mevzuatla teyit edilmeli)

> Aşağıdaki süreler kodda **uygulanmaktadır** (bkz. §5). Değerler `RETENTION_*` ortam
> değişkenleriyle işletme/mevzuat ihtiyacına göre ayarlanabilir.

| Veri / Tablo | İçerik | Uygulanan Süre (varsayılan) | İmha Yöntemi | Gerekçe |
|--------------|--------|------------------------------|--------------|---------|
| `call_logs` + `conversation_turns` | Çağrı meta + transkript | 365 gün (`RETENTION_CALL_LOG_DAYS`) | Satır silme | Hizmet/uyuşmazlık |
| Ses kayıtları | Görüşme kaydı | 180 gün (`RETENTION_RECORDING_DAYS`) | URL null + MinIO ILM dosya imhası | Kalite/uyuşmazlık; veri minimizasyonu |
| `consents` | Açık rıza/onay kaydı | **İmha edilmez** | — | İspat yükü |
| `appointments` | Randevu | 365 gün (`RETENTION_PII_DAYS`) | Ad+telefon anonimleştirme (satır kalır) | Sözleşme ifası + istatistik |
| `orders` | Sipariş | 365 gün (`RETENTION_PII_DAYS`) | Ad+telefon anonimleştirme (satır kalır) | Sözleşme ifası + istatistik |
| `message_logs` | Giden mesaj | 365 gün (`RETENTION_MESSAGE_LOG_DAYS`) | Satır silme | İspat/uyuşmazlık |
| `invoices` | Fatura | **İmha edilmez** | — | **Vergi mevzuatı (VUK, örn. 5 yıl)** |
| Tenant hesap verisi | İşletme/sahip | Hizmet ilişkisi + mevzuat süresi (manuel) | Soft-delete | Sözleşme/vergi |

## 3. İmha Yöntemleri

- **Silme:** İlgili kullanıcılar için verinin erişilemez/kullanılamaz hâle getirilmesi (DB satır
  silme, soft-delete sonrası kalıcı temizlik).
- **Yok etme:** Fiziksel/manyetik ortamların geri dönüşümsüz biçimde imhası.
- **Anonimleştirme:** İstatistik/iyileştirme amacıyla kimliği belirlenemez hâle getirme (özellikle
  transkript/ses analizi için tercih edilir).

## 4. Periyodik İmha

Saklama süresi dolan veriler için periyodik imha **günde bir** (`RETENTION_SCAN_INTERVAL_HOURS=24`)
otomatik uygulanır. Her imha taraması `retention_runs` tablosuna denetim satırı yazar
(ne zaman, hangi tablodan kaç kayıt).

## 5. Uygulama (Teknik)

Politika koda dökülmüştür — `backend/src/VoiceReception.Api/Retention/RetentionSweeper.cs`:

- **RetentionSweeper** (arka plan servisi, `RETENTION_ENABLED=true` varsayılan): her taramada
  sırasıyla (1) süresi dolan ses kaydı URL'lerini null'lar, (2) süresi dolan `call_logs` +
  `conversation_turns` satırlarını siler, (3) süresi dolan `message_logs` satırlarını siler,
  (4) süresi dolan `appointments`/`orders` kayıtlarında ad+telefonu anonimleştirir
  (`[silindi]`; soft-delete edilmişler dahil), (5) `retention_runs` denetim satırı yazar.
- **Consent ve Invoice bilinçli olarak atlanır** (rıza ispat yükü; vergi mevzuatı).
- **Ses dosyasının fiziksel imhası:** DB yalnız URL referansını temizler; dosyayı MinIO bucket
  lifecycle kuralı siler: `mc ilm rule add local/telesekreter-recordings --expire-days 180`
  (gün sayısı `RETENTION_RECORDING_DAYS` ile eşleşmeli).
- **DSAR (KVKK m.11 silme talebi):** `POST /api/tenants/{tenantId}/dsar/erase` gövde
  `{ "phone": "+90..." }` — o telefonun çağrı logu telefonu anonimleştirilir, transkript turları
  silinir, randevu/sipariş ad+telefonu anonimleştirilir, mesaj logu telefonu anonimleştirilir.
  Consent ve Invoice korunur. Uç idempotenttir; tablo başına sayı döner (talep yanıtına kanıt).
- Ortam değişkenleri: `backend/.env.example` → `RETENTION_*` bloğu.
