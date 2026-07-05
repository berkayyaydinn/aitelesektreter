namespace VoiceReception.Api.Domain;

/// <summary>Tenant'a tahsis edilen DID. Müşteri GSM'i buraya yönlendirir (**21*DID#).</summary>
public class PhoneNumber
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>SIP sağlayıcıdaki DID. Çağrı routing anahtarı (tenant'a eşleme).</summary>
    public string Did { get; set; } = string.Empty;

    public ForwardingStatus ForwardingStatus { get; set; } = ForwardingStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ForwardingStatus
{
    Pending = 0,   // tenant henüz yönlendirme açmadı
    Active = 1,    // test çağrısı ile doğrulandı
    Failed = 2,
}
