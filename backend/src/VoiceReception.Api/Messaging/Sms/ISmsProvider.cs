namespace VoiceReception.Api.Messaging.Sms;

/// <summary>Düz metin SMS gönderim soyutlaması (swappable: console bugün, Netgsm üretim).
///
/// IMessagingProvider'dan ayrı: SMS düz metindir, Meta template/parametre modeli yoktur.
/// Hatırlatma dağıtıcısı (ReminderDispatcher) bunu tüketir; MessageLog denetimi + Consent kapısı
/// çağıran tarafta ortak kalır.
/// </summary>
public interface ISmsProvider
{
    string Channel { get; }   // "console-sms" | "sms"

    Task<SmsResult> SendAsync(string toPhone, string text, CancellationToken ct = default);
}

public record SmsResult(bool Success, string? ProviderMessageId, string? Error);
