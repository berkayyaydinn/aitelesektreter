# Hukuki / Fikri Mülkiyet (IP) Notları

> ⚠️ **Bu hukuki tavsiye DEĞİLDİR.** Gerçek patent/marka kliringi yalnızca **TÜRKPATENT** resmi
> araması + **IP/marka avukatı** ile yapılır. Aşağıdakiler yön gösterici bir çerçeve ve checklist'tir.

## IP Türleri — Ne Neyi Korur?

| Tür | Korur | Bu projeyle ilgisi |
|-----|-------|--------------------|
| **Marka** | İsim, logo, slogan | **En yakın risk.** Ürün/marka adı seçimi kliring ister. |
| **Patent / Faydalı Model** | Yeni, buluş basamağı içeren teknik **yöntem** | Düşük: stack standart 3. parti, yeni metot iddiası yok. |
| **Tasarım (Endüstriyel)** | Görsel arayüz/ürün görünümü | UI geliştirilince ilgili olur. |
| **Telif** | Kaynak kod, metin | Kod zaten bize ait; başkasının kodunu kopyalamıyoruz. |

## Gerçekçi Risk Değerlendirmesi

- **Genel fikir** (AI telesekreter / randevu botu / çağrıyı AI'a yönlendirme) çok yaygın →
  **bol prior art**. Geniş fikir patentlenemez ve tek bir patentle bloklanması zor.
- **Asıl risk markadır** (isim/logo). Mevcut bir ticari ürünün **adına benzer** ad seçmek sorun olur;
  jenerik/betimleyici kelimeler (ör. "telesekreter") kimseye ait değildir.
- **Teknik ihlal maruziyeti düşük (tasarım gereği):** LiveKit, SIP yönlendirme, STT/LLM/TTS,
  WhatsApp Cloud API gibi **standart/3. parti bileşenler** kullanılır; özgün patentli bir yöntem
  iddia edilmez. Bu, savunmacı prior-art duruşudur.

## Yapılması Gerekenler (checklist)

### Marka (öncelik)
- [ ] Nihai ürün/marka adını belirle (codename `VoiceReception` placeholder).
- [ ] **TÜRKPATENT marka araması** — ilgili Nice sınıfları: **9** (yazılım), **38** (telekomünikasyon),
      **42** (SaaS/yazılım hizmetleri); muhtemelen **35** (iş/çağrı merkezi hizmetleri).
- [ ] Benzer/karıştırılabilir marka var mı? Sessel + görsel + anlamsal benzerlik.
- [ ] Alan adı (.com / .com.tr) + ticaret unvanı uygunluğu.
- [ ] Seçilen adı **tescil ettir** (avukat ile).

### Patent (düşük ama kontrol)
- [ ] TÜRKPATENT patent/faydalı model araması + **Espacenet** + **Google Patents**.
      Anahtarlar: "yapay zeka çağrı yönlendirme", "AI voice agent appointment", "IVR randevu".
- [ ] Kendi tarafımızda **yeni teknik yöntem iddia etmeyeceğimizi** belgele (savunmacı yayın).
      Standart bileşen kombinasyonu = düşük buluş basamağı = düşük ihlal riski.

### Genel
- [ ] KVKK/İYS uyumu (bkz. `compliance-iys.md`, `architecture.md`) — hukuki yükümlülük, IP'den ayrı.
- [ ] Üçüncü parti lisansları (LiveKit, modeller, kütüphaneler) ticari kullanım için uygun mu?

## Teknik Farklılaştırma (taklit etmeme + özgün değer)

Mevcut TR sesli asistan / çağrı merkezi ürünlerine benzememek ve savunulabilir kendi değerini
oluşturmak için **bilinçli tasarım tercihleri**. (Bunlar farklılaştırıcı tercihtir; patent iddiası değil.)

1. **Ek numara olmadan dönüşüm** — koşullu çağrı yönlendirme + işletme tarafında **mesai modu toggle**;
   numara taşıma/yeni hat zorunluluğu yok.
2. **Birleşik çapraz-kanal onay defteri** — KVKK çağrı kaydı onayı + İYS kampanya onayı **tek
   `Consent` modelinde**; uyum kapısı (`IysComplianceService`) aramayı baştan bloklar.
3. **Tam swappable mimari** — STT/LLM/TTS/mesajlaşma/DB/dialer hepsi interface arkasında, `.env` ile
   değişir. Tek satıcıya kilitlenme yok; rakiplerin çoğu tek yığına bağlı.
4. **DID→tenant backend routing** — numara-bağımsız çok-kiracılı ölçekleme; tenant config çağrı
   anında backend'den.
5. **Türkçe-öncelikli, telefon 8kHz'e ayarlı** ses hattı + eval seti yaklaşımı (`spikes.md`).

> Farklılaştırma stratejisini güçlendirmek için ürün/pazar konumlandırması ayrıca yapılabilir
> (bu doküman hukuki/teknik kapsamda; pazar farklılaştırma ayrı bir çalışmadır).

## Codename Politikası

- Kod/namespace içi ad: **`VoiceReception`** (betimleyici, nötr, iç codename).
- Dokümanlarda "telesekreter / sesli karşılama" **jenerik isim** olarak kullanılır (özellik kategorisi).
- **Pazara çıkış adı ayrıdır** ve yukarıdaki marka kliringinden geçmeden kullanılmamalıdır.
