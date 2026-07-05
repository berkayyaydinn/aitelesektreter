using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Invoicing;

/// <summary>Fatura kesme soyutlaması (swappable).
///
/// Dry-run: <see cref="ConsoleInvoiceProvider"/> (log'a yazar). Üretim: GİB e-Arşiv/e-Fatura veya
/// Paraşüt/Logo gibi bir sağlayıcı entegrasyonu eklenir — çağıran taraf IInvoiceProvider görür.
/// </summary>
public interface IInvoiceProvider
{
    Task<InvoiceResult> IssueAsync(Invoice invoice, CancellationToken ct = default);
}

public record InvoiceResult(bool Success, string? ProviderInvoiceId, string? Error);
