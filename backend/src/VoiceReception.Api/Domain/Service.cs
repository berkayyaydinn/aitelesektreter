namespace VoiceReception.Api.Domain;

/// <summary>Randevu/sipariş alınabilen hizmet kalemi (ör. "Saç kesimi", 30 dk).</summary>
public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Randevu süresi (dk). Slot hesaplaması bunu kullanır.</summary>
    public int DurationMinutes { get; set; } = 30;

    public bool IsActive { get; set; } = true;
    /// <summary>Soft-delete: true ise tüm sorgulardan gizlenir.</summary>
    public bool IsDeleted { get; set; }
}
