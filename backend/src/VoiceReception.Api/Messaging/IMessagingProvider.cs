namespace VoiceReception.Api.Messaging;

/// <summary>Giden bilgilendirme mesajı soyutlaması (swappable: WhatsApp bugün, Instagram yarın).</summary>
public interface IMessagingProvider
{
    string Channel { get; }

    /// <summary>Onaylı template ile bilgilendirme gönderir (24 saat penceresi dışı zorunlu).</summary>
    Task<MessageResult> SendTemplateAsync(
        string toPhone,
        string templateName,
        IReadOnlyList<string> parameters,
        CancellationToken ct = default);
}

public record MessageResult(bool Success, string? ProviderMessageId, string? Error);
