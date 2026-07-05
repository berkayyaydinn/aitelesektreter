# Kişisel Veri Saklama ve İmha Politikası

> **Hukuki sorumluluk reddi:** Bu metin bir taslak/şablondur ve hukuki danışmanlık niteliği
> taşımaz. Saklama süreleri ilgili mevzuata ve işletme ihtiyacına göre bir avukatla teyit
> edilmelidir. `[...]` alanları doldurulmalıdır.

Bu politika, KVKK ve Kişisel Verilerin Silinmesi, Yok Edilmesi veya Anonim Hâle Getirilmesi
Hakkında Yönetmelik uyarınca verilerin saklanması ve imhasını düzenler.

## 1. Kapsam

AI Telesekreter / VoiceReception altyapısında işlenen tüm kişisel veriler (bkz. veri modeli,
`docs/architecture.md`).

## 2. Saklama Süreleri (Öneri — mevzuatla teyit edilmeli)

> Kodda şu an otomatik silme/imha **yok**; veriler süresiz tutuluyor. Aşağıdaki süreler öneridir
> ve hayata geçirilmesi gerekir.

| Veri / Tablo | İçerik | Önerilen Saklama Süresi | Gerekçe |
|--------------|--------|--------------------------|---------|
| `call_logs` | Çağrı meta, transkript ref. | [örn. 1 yıl] | Hizmet/uyuşmazlık |
| Ses kayıtları | Görüşme kaydı | [örn. 6 ay – mümkün olan en kısa] | Kalite/uyuşmazlık; veri minimizasyonu |
| `consents` | Açık rıza/onay kaydı | Rızanın geçerlilik süresi + ispat süresi | İspat yükü |
| `appointments` | Randevu | Hizmet ilişkisi + [makul süre] | Sözleşme ifası |
| `orders` | Sipariş | Hizmet ilişkisi + [makul süre] | Sözleşme ifası |
| `message_logs` | Giden mesaj | [örn. 1 yıl] | İspat/uyuşmazlık |
| `invoices` | Fatura | **Vergi mevzuatı (örn. 5 yıl)** | Hukuki yükümlülük |
| Tenant hesap verisi | İşletme/sahip | Hizmet ilişkisi + mevzuat süresi | Sözleşme/vergi |

## 3. İmha Yöntemleri

- **Silme:** İlgili kullanıcılar için verinin erişilemez/kullanılamaz hâle getirilmesi (DB satır
  silme, soft-delete sonrası kalıcı temizlik).
- **Yok etme:** Fiziksel/manyetik ortamların geri dönüşümsüz biçimde imhası.
- **Anonimleştirme:** İstatistik/iyileştirme amacıyla kimliği belirlenemez hâle getirme (özellikle
  transkript/ses analizi için tercih edilir).

## 4. Periyodik İmha

Saklama süresi dolan veriler için **periyodik imha** [örn. 6 ayda bir] uygulanır. İmha işlemleri
kayıt altına alınır.

## 5. Uygulama Notu (Teknik Borç)

- Tablolara saklama/imha alanı ve otomatik temizlik işi (cron/job) eklenmesi gerekir.
- Ses kayıtları için en kısa saklama + erken anonimleştirme veri minimizasyonu açısından önerilir.
