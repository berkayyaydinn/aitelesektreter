namespace VoiceReception.Api.Messaging;

/// <summary>Dry-run mesaj sağlayıcı — gerçek Meta yokken kullanılır.
///
/// Mesajı göndermez, log'a yazar ve başarı döner. Böylece randevu sonrası bilgilendirme akışı
/// (MessageLog dahil) Meta onayı/token olmadan uçtan uca test edilebilir.
/// MESSAGING_PROVIDER=console ile seçilir (varsayılan lokal mod).
/// </summary>
public class ConsoleMessagingProvider : IMessagingProvider
{
    private readonly ILogger<ConsoleMessagingProvider> _logger;

    public string Channel => "console";

    public ConsoleMessagingProvider(ILogger<ConsoleMessagingProvider> logger) => _logger = logger;

    public Task<MessageResult> SendTemplateAsync(
        string toPhone, string templateName, IReadOnlyList<string> parameters, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DRY-RUN mesaj] -> {Phone} | template={Template} | parametreler=[{Params}]",
            toPhone, templateName, string.Join(", ", parameters));

        return Task.FromResult(new MessageResult(true, $"dryrun-{Guid.NewGuid():N}", null));
    }
}
