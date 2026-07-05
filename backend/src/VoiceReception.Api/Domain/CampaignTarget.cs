namespace VoiceReception.Api.Domain;

/// <summary>Kampanya arama listesindeki tek kayıt. İYS/saat kapısına takılırsa Skipped + neden.</summary>
public class CampaignTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public Guid TenantId { get; set; }

    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    public TargetStatus Status { get; set; } = TargetStatus.Pending;

    /// <summary>Skipped/Failed ise sebep (ör. "İYS onayı yok", "İzinli saat dışı").</summary>
    public string? SkipReason { get; set; }

    public DateTime? LastAttemptUtc { get; set; }
}

public enum TargetStatus
{
    Pending = 0,
    Skipped = 1,    // İYS/saat kapısı engelledi
    Completed = 2,  // arama yapıldı
    Failed = 3,     // arama başarısız
}
