# KVKK / Yasal Metinler

> **Hukuki sorumluluk reddi:** Bu klasördeki tüm metinler **taslak/şablondur**, hukuki danışmanlık
> değildir. Yürürlüğe almadan önce KVKK uzmanı bir avukatla gözden geçirilmelidir.

## Dosyalar

| Dosya | Amaç | Rol |
|-------|------|-----|
| `kvkk-aydinlatma-arayan.md` | Arayan müşteri aydınlatma metni | İşletme = sorumlu, platform = işleyen |
| `kvkk-aydinlatma-isletme.md` | İşletme (tenant) aydınlatma metni | Platform = sorumlu |
| `kvkk-acik-riza.md` | Çağrı kaydı + kampanya (İYS) açık rıza metinleri | — |
| `veri-isleme-sozlesmesi-dpa.md` | Veri İşleyen Sözleşmesi (KVKK m.12) | Platform ↔ işletme |
| `saklama-imha-politikasi.md` | Saklama ve imha politikası | İç doküman |

## NEREYE GÖNDERİLİR / SUNULUR

> **Kritik:** KVKK **aydınlatma** ve **açık rıza** metinleri hiçbir devlet kurumuna
> **GÖNDERİLMEZ**. Veri sahiplerine *sunulur/gösterilir*. Yalnızca **VERBİS** ve (varsa) **İYS**
> kayıtları ilgili kurumlara yapılır.

| Doküman | Nereye / kime | Nasıl |
|---------|---------------|-------|
| Arayan aydınlatma metni | Arayan müşteriye | Çağrı anonsu + erişilebilir kanal (web/QR/SMS bağlantısı). Kuruma gönderilmez. |
| İşletme aydınlatma metni | Tenant'a | Onboarding/kayıt ekranında sunulur. Kuruma gönderilmez. |
| Açık rıza (çağrı kaydı) | Arayandan alınır | Anons sonrası onay → `consents` tablosuna kayıt. Saklanır. |
| Açık rıza (kampanya/İYS) | İlgili kişiden alınır | **İleti Yönetim Sistemi**'ne (https://iys.org.tr) kaydedilir. |
| Veri İşleyen Sözleşmesi (DPA) | Platform ↔ işletme | İmza/elektronik kabul. Talep hâlinde ibraz. |
| Saklama-İmha Politikası | İç doküman | VERBİS kaydı varsa beyan; talep hâlinde Kurum'a ibraz. |
| **VERBİS kaydı** | **Kişisel Verileri Koruma Kurumu** | https://verbis.kvkk.gov.tr — *muaf değilse*. |

## VERBİS Kaydı — Şahıs Şirketi İçin

VERBİS (Veri Sorumluları Sicil Bilgi Sistemi) kaydı, **muafiyet kapsamına girmeyen** veri
sorumluları için zorunludur. Tipik muafiyet ölçütü: yıllık çalışan sayısı **< 50** **ve** yıllık
mali bilanço eşik altı **ve** ana faaliyetin özel nitelikli veri işleme olmaması.

> ⚠️ **Dikkat:** Bu hizmette **sistematik ses kaydı ve kişisel veri işleme ana faaliyettir**.
> Bu nedenle muafiyet **tartışmalıdır** ve kayıt yükümlülüğü doğabilir. Kararı mutlaka bir **KVKK
> danışmanı/avukatı** ile teyit edin.
>
> Not: Ses kaydı **tek başına** özel nitelikli (biyometrik) kişisel veri sayılmaz; olağan kişisel
> veridir.

## Doldurulması Gereken Yer Tutucular

Tüm metinlerde geçen `[...]` alanları yürürlük öncesi doldurulmalı:

- `[İŞLETME SAHİBİ AD-SOYAD]`
- `[ŞAHIS ŞİRKETİ UNVANI]` / `[İŞLETME UNVANI]`
- `[ADRES]`
- `[VERGİ DAİRESİ/NO]`
- `[TELEFON]`
- `[E-POSTA / KVKK BAŞVURU ADRESİ]`
- `[WEB SİTESİ / AYDINLATMA ERİŞİM KANALI]`
- `[SAKLAMA SÜRELERİ]`
