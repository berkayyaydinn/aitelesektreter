namespace VoiceReception.Api.Messaging.Sms;

/// <summary>Dry-run SMS sağlayıcı — gerçek Netgsm yokken kullanılır.
///
/// SMS göndermez, log'a yazar, başarı döner. Hatırlatma akışı (MessageLog dahil) Netgsm
/// kimlik bilgisi olmadan uçtan uca test edilebilir. SMS_PROVIDER=console ile seçilir (varsayılan).
/// </summary>
public class ConsoleSmsProvider : ISmsProvider
{
    private readonly ILogger<ConsoleSmsProvider> _logger;

    public string Channel => "console-sms";

    public ConsoleSmsProvider(ILogger<ConsoleSmsProvider> logger) => _logger = logger;

    public Task<SmsResult> SendAsync(string toPhone, string text, CancellationToken ct = default)
    {
        _logger.LogInformation("[DRY-RUN sms] -> {Phone} | {Text}", toPhone, text);
        return Task.FromResult(new SmsResult(true, $"dryrun-sms-{Guid.NewGuid():N}", null));
    }
}
