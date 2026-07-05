namespace VoiceReception.Api.Domain;

/// <summary>Telesekreter kullanan işletme (multi-tenant kök kayıt).</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BusinessName { get; set; } = string.Empty;

    /// <summary>Ses ajanına eklenecek işletmeye özel ek talimat (opsiyonel).</summary>
    public string? ExtraPrompt { get; set; }

    /// <summary>İşletme sahibinin telefon numarası. Bu numaradan arandığında sahip moduna geçilir
    /// (fatura kesme). Sahip doğrulaması bununla yapılır.</summary>
    public string? OwnerPhone { get; set; }

    public bool IsActive { get; set; } = true;
    /// <summary>Soft-delete: true ise tüm sorgulardan gizlenir (geçmiş korunur, silinmez).</summary>
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PhoneNumber> PhoneNumbers { get; set; } = new List<PhoneNumber>();
    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<BusinessHour> BusinessHours { get; set; } = new List<BusinessHour>();
}
