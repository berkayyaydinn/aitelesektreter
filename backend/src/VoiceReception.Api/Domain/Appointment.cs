namespace VoiceReception.Api.Domain;

/// <summary>Sesli ajanın aldığı randevu.</summary>
public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ServiceId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;
    /// <summary>Soft-delete: true ise tüm sorgulardan gizlenir.</summary>
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hatırlatma SMS'i gönderildiği an (UTC). null = henüz hatırlatılmadı.
    /// Idempotensi: dağıtıcı yalnız null olanları seçer, başarılı gönderimde set eder.</summary>
    public DateTime? ReminderSentAt { get; set; }
}

public enum AppointmentStatus
{
    Booked = 0,
    Cancelled = 1,
    Completed = 2,
}
