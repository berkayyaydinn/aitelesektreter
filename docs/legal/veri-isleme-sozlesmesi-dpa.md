# Veri İşleyen Sözleşmesi (DPA)

> **Hukuki sorumluluk reddi:** Bu metin bir taslak/şablondur ve hukuki danışmanlık niteliği
> taşımaz. Yürürlüğe almadan önce KVKK alanında uzman bir avukatla gözden geçirilmesi
> zorunludur. `[...]` alanları doldurulmalıdır.

KVKK m.12/2 uyarınca, veri sorumlusu adına veri işleyen tarafça yürütülen işleme faaliyetinin
yazılı bir sözleşmeye dayanması gerekir. Bu sözleşme işletme (tenant) onboarding'inde elektronik
olarak kabul ettirilir.

## 1. Taraflar

- **Veri Sorumlusu:** Hizmeti kullanan işletme — **[İŞLETME UNVANI]** ("Sorumlu").
- **Veri İşleyen:** **[ŞAHIS ŞİRKETİ UNVANI]** — AI Telesekreter / VoiceReception ("İşleyen").

## 2. Konu ve Kapsam

İşleyen; Sorumlu'nun müşterilerine (arayanlara) ait kişisel verileri, yalnızca sesli karşılama,
randevu/sipariş yönetimi ve bilgilendirme mesajları amacıyla, Sorumlu adına ve talimatlarıyla işler.

**İşlenen veri kategorileri:** telefon numarası, ses kaydı, görüşme transkripti, ad-soyad,
randevu/sipariş içeriği. **İlgili kişi grupları:** Sorumlu'nun müşterileri/arayanları.

## 3. İşleyenin Yükümlülükleri

1. Verileri yalnızca Sorumlu'nun **yazılı/elektronik talimatları** ve bu sözleşme doğrultusunda
   işlemek; başka amaçla kullanmamak.
2. KVKK m.12 kapsamında **uygun teknik ve idari tedbirleri** almak (erişim kontrolü, internal API
   anahtar doğrulaması, aktarımda/saklamada güvenlik).
3. İşlemeye yetkili personelin **gizlilik** taahhüdü altında olmasını sağlamak.
4. **Alt-işleyen** kullanımını bu sözleşme ile bildirmek (bkz. Ek-1) ve alt-işleyenlere eşdeğer
   yükümlülük yüklemek.
5. **Veri ihlali** hâlinde Sorumlu'yu gecikmeksizin bilgilendirmek (KVKK m.12/5).
6. İlgili kişi başvuru ve haklarının (m.11) kullanılmasında Sorumlu'ya **yardımcı olmak**.
7. Hizmet sona erdiğinde verileri Sorumlu'ya **iade etmek veya imha etmek** (Sorumlu'nun tercihine
   göre), mevzuatın zorunlu kıldığı saklama hâlleri saklı kalmak üzere.

## 4. Sorumlu'nun Yükümlülükleri

- İlgili kişilere KVKK m.10 aydınlatmasını yapmak (`kvkk-aydinlatma-arayan.md`),
- Gerekli açık rıza/onayların alınmasını sağlamak,
- İşleyene verdiği talimatların hukuka uygunluğunu temin etmek.

## 5. Yurt Dışı Aktarım

İşleyen, hizmetin sunulması için verileri Ek-1'deki yurt dışı alt-işleyenlere KVKK m.9 kapsamında
aktarabilir. Sorumlu bu aktarımdan haberdardır ve ilgili kişilere bunu aydınlatma metninde bildirir.

## 6. Süre ve Fesih

Bu sözleşme, hizmet sözleşmesi yürürlükte olduğu sürece geçerlidir. Fesih hâlinde m.3/7 uygulanır.

---

## Ek-1: Alt-İşleyen Listesi

| Alt-İşleyen | Hizmet | Veri | Konum |
|-------------|--------|------|-------|
| OpenAI | LLM / (opsiyonel STT) | transkript, ses | Yurt dışı |
| Deepgram | STT (yapılandırmaya göre) | ses | Yurt dışı |
| Microsoft Azure | STT/TTS (yapılandırmaya göre) | ses | Yurt dışı |
| ElevenLabs | TTS (yapılandırmaya göre) | metin/ses | Yurt dışı |
| Meta Platforms | WhatsApp Business Cloud API | telefon, mesaj | Yurt dışı |
| [SIP / Bulut SağlayıcI] | Çağrı / barındırma | çağrı meta, kayıt | [KONUM] |

> Üretimde aktif kullanılan sağlayıcılar `.env` yapılandırmasına göre güncellenmelidir.
