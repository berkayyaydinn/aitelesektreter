namespace VoiceReception.Api.Domain;

/// <summary>Giden WhatsApp/Instagram mesaj kaydı (idempotensi + denetim).</summary>
public class MessageLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Channel { get; set; } = "whatsapp";   // whatsapp | instagram
    public string ToPhone { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;

    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum MessageStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
}
