# İşletme Onboarding — Çağrı Yönlendirme

İşletme **kendi GSM numarasını ek numara almadan** telesekretere bağlar. Yöntem: çağrı yönlendirme.
Backend her tenant'a bir **DID** tahsis eder; işletme bu DID'e yönlendirir.

## Adımlar

1. Kayıt sonrası uygulama tenant'a bir **DID** gösterir (ör. `0850 XXX XX XX`).
2. İşletme kendi telefonundan yönlendirme kodunu çevirir.
3. Uygulama **test çağrısı** ile yönlendirmeyi doğrular.

## Yönlendirme Kodları (GSM)

| Mod | Kod | Davranış |
|-----|-----|----------|
| **Koşulsuz (önerilen)** | `**21*DID#` çevir (ara tuşu) | TÜM aramalar AI'a gider |
| Koşulsuz kapat | `##21#` | Yönlendirmeyi iptal eder |
| Meşgulde | `**67*DID#` | Sadece meşgulken AI'a |
| Cevapsızda | `**61*DID#` | Sadece cevap verilmezse AI'a |
| Ulaşılamazda | `**62*DID#` | Kapalı/çekmediğinde AI'a |

> `DID` yerine işletmeye atanan numara yazılır (başında `0` ile, boşluksuz).

## Önemli Tradeoff

**Koşulsuz yönlendirme** = işletme o hattan gelen aramaları **kendisi alamaz** (hepsi AI'a gider).
Çözümler:
- **Mesai modu:** İşletme yönlendirmeyi sadece istediği saatlerde açar (`**21*DID#` / `##21#`).
- **Ayrı iş hattı/eSIM:** Müşteri aramaları için ayrı numara, kişisel hat ayrı kalır.
- **Koşullu mod:** Sadece cevapsız/meşgulde AI devreye girer (operatöre göre değişken, test gerekir).

## Operatör Notları

- **Turkcell / Vodafone / Türk Telekom:** `**21*` koşulsuz yönlendirme standart çalışır.
- Koşullu kodlar (`**61/**62/**67`) bazı operatörlerde önce **operatör sesli mesajına** düşebilir
  → varsayılan koşulsuz; koşullu isteyen tenant'lar için test-çağrı zorunlu.

## Doğrulama

Yönlendirme açıldıktan sonra uygulama DID'e bir test çağrısı tetikler / işletmeden numarasını
aramasını ister. Backend gelen çağrıyı algılarsa `phone_numbers.forwarding_status = active`.
