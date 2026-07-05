namespace VoiceReception.Api.Domain;

/// <summary>Haftalık çalışma saati. Slot üretimi bu aralıkla sınırlanır.</summary>
public class BusinessHour
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public DayOfWeek Day { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public bool IsClosed { get; set; }
}
