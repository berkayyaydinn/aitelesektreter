using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Invoicing;

/// <summary>Dry-run fatura sağlayıcı — gerçek GİB/sağlayıcı yok, log'a yazar.
/// Sahip doğrulama + fatura akışı lokal test edilir. INVOICE_PROVIDER=console (varsayılan).</summary>
public class ConsoleInvoiceProvider : IInvoiceProvider
{
    private readonly ILogger<ConsoleInvoiceProvider> _logger;

    public ConsoleInvoiceProvider(ILogger<ConsoleInvoiceProvider> logger) => _logger = logger;

    public Task<InvoiceResult> IssueAsync(Invoice invoice, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DRY-RUN fatura] {Amount} {Currency} -> {Customer} | {Desc}",
            invoice.Amount, invoice.Currency, invoice.CustomerName, invoice.Description);

        return Task.FromResult(new InvoiceResult(true, $"dryrun-inv-{Guid.NewGuid():N}", null));
    }
}
