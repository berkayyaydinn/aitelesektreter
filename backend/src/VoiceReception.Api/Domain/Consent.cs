namespace VoiceReception.Api.Domain;

/// <summary>KVKK / İYS onay kaydı. Çağrı kaydı bildirimi ve (ileride) kampanya onayı.</summary>
public class Consent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string CustomerPhone { get; set; } = string.Empty;
    public ConsentType Type { get; set; }
    public string Source { get; set; } = string.Empty;   // ör. "call_announcement", "iys"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ConsentType
{
    CallRecording = 0,        // KVKK çağrı kaydı bildirimi
    OutboundCampaign = 1,     // İYS — giden kampanya araması (ertelenmiş faz)
    TransactionalSms = 2,     // Hizmet/işlem SMS'i (randevu & ödeme hatırlatma)
}
