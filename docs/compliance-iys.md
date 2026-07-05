# Giden Kampanya + İYS Uyumu

Giden ticari arama Türkiye'de **İYS (İleti Yönetim Sistemi)** onayı + izinli arama saati ister.
Bu, yasal olarak ZORUNLU bir kapıdır — `CampaignRunner` her aramadan önce uygular.

## Akış

```
Kampanya çalıştır
   └─ her hedef için:
        IysComplianceService.Evaluate(tenant, telefon, yerelSaat)
          ├─ İYS onayı yok        -> Skipped + "İYS onayı yok"      (ARAMA YOK)
          ├─ izinli saat dışı     -> Skipped + "İzinli saat dışı"   (ARAMA YOK)
          └─ izinli               -> IOutboundDialer.PlaceCall      (ara)
```

## Bileşenler

| Bileşen | Sorumluluk | Swappable |
|---------|-----------|-----------|
| `IIysClient` | İYS onay sorgusu | `LocalIysClient` (Consents tablosu) → üretim: gerçek İYS API |
| `IysComplianceService` | onay + saat kapısı | — (yasal kritik mantık) |
| `IOutboundDialer` | arama yerleştirme | `ConsoleOutboundDialer` (dry-run) → üretim: LiveKit SIP outbound |
| `CampaignRunner` | hedefleri yürüt | — |

## İzinli Saat Penceresi

`IysComplianceService.AllowedStart/End` = **09:00–21:00** (Türkiye yerel, UTC+3).

> ⚠️ Bu değerler **yasal olarak doğrulanmalı**. Ticari arama saat kısıtları mevzuata tabidir;
> uygulamadan önce güncel reklam/çağrı mevzuatı + İYS kuralları teyit edilmeli. Hafta sonu/resmi
> tatil kısıtları eklenebilir.

## API

| Metot | Yol | Amaç |
|-------|-----|------|
| POST | `/api/tenants/{id}/campaigns` | Kampanya oluştur |
| POST | `/api/campaigns/{id}/targets` | Müşteri listesi içe aktar |
| POST | `/api/campaigns/{id}/consents` | İYS onayı kaydet/içe aktar (lokal/test) |
| POST | `/api/campaigns/{id}/run` | Yürüt (İYS/saat kapısı uygulanır) → özet |
| GET | `/api/campaigns/{id}` | Durum + hedef dağılımı |

## Lokal Test

Backend lokal modda (`MESSAGING_PROVIDER=console`, `OUTBOUND_DIALER=console`). İYS onayı
`/consents` ile eklenir; `run` çıktısı `{ called, skipped, failed, reasons }`. Onaysız numaralar
`reasons["İYS onayı yok"]` altında sayılır, ARANMAZ. Dry-run arama log'a yazılır:
`[DRY-RUN arama] kampanya=... -> Ali <+90...>`.

Testler: `backend/tests/VoiceReception.Tests/IysComplianceTests.cs` (onay yok / saat dışı / izinli / runner).

## Üretime Geçiş (İSKELE → gerçek)

- [ ] `IysApiClient` — gerçek İYS API onay sorgusu (`LocalIysClient` yerine).
- [ ] `LiveKitOutboundDialer` — LiveKit SIP outbound (createSIPParticipant) + kampanya prompt'lu agent.
- [ ] Senkron `CampaignRunner` yerine arka plan kuyruğu + tenant bazlı hız sınırı + yeniden deneme.
- [ ] İzinli saat penceresi + tatil kuralları mevzuatla doğrula; tenant TZ.
- [ ] Onay geri çekme (opt-out) ve arama kayıt/raporlama (denetim).
