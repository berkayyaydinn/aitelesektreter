# Telefonla Fatura Kesme (Sahip Modu)

İşletme sahibi kendi numarasından aradığında sesli ajan **sahip moduna** geçer ve sesli komutla
fatura kesebilir. Müşteriler bu moda giremez.

## Sahip Doğrulama (iki kat)

1. **Voice worker** — arayan numarayı (`extract_caller`) tenant'ın `ownerPhone`'u ile karşılaştırır;
   eşitse `create_invoice` aracı + sahip modu promptu eklenir.
2. **Backend** — `POST /internal/invoices` ayrıca `callerPhone == tenant.OwnerPhone` doğrular;
   değilse **403** (savunmada derinlik — worker atlansa bile backend reddeder).

## Akış

```
Sahip arar → DID→tenant → caller == ownerPhone? 
   └─ evet → create_invoice aracı aktif
        "Ali'ye 1500 TL danışmanlık faturası kes" 
          → POST /internal/invoices {callerPhone, customerName, amount, description}
          → IInvoiceProvider.Issue (dry-run/console)  → Invoice kaydı (Issued)
```

## Bileşenler

| Bileşen | Sorumluluk | Swappable |
|---------|-----------|-----------|
| `Tenant.OwnerPhone` | sahip numarası | — |
| `Invoice` | fatura kaydı | — |
| `IInvoiceProvider` | fatura kesme | `ConsoleInvoiceProvider` (dry-run) → üretim: GİB e-Arşiv / Paraşüt / Logo |
| `InternalApiEndpoints.CreateInvoice` | owner-auth + persist | — |

## Onboarding

Tenant oluştururken `ownerPhone` verilir:
```json
POST /api/tenants {"businessName":"...","did":"0850...","ownerPhone":"+90555..."}
```

## Lokal Test

`MESSAGING_PROVIDER`/`OUTBOUND_DIALER` gibi `IInvoiceProvider` de console dry-run. Fatura log'a yazılır:
`[DRY-RUN fatura] 1500.5 TRY -> Ali | Hizmet`. Test: smoke_test (owner→Issued, non-owner→403) +
`ApiIntegrationTests.Invoice_only_owner_can_issue`.

## Üretime Geçiş

- [ ] `GibInvoiceProvider` / `ParasutInvoiceProvider` — gerçek e-Arşiv/e-Fatura entegrasyonu.
- [ ] Sahip kimliği güçlendirme — numara spoofing'e karşı ek doğrulama (sesli PIN / OTP).
- [ ] Fatura iptal/iade, KDV/vergi alanları, PDF/UBL.
