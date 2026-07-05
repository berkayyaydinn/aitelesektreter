namespace VoiceReception.Api.Domain;

/// <summary>Giden kampanya — verilen müşteri listesini arayıp bilgilendirme yapar.
/// Her arama öncesi İYS onayı + izinli saat kontrolü zorunlu (CampaignRunner uygular).</summary>
public class Campaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Sesli ajanın kampanyada söyleyeceği script/prompt.</summary>
    public string ScriptPrompt { get; set; } = string.Empty;

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignTarget> Targets { get; set; } = new List<CampaignTarget>();
}

public enum CampaignStatus
{
    Draft = 0,
    Running = 1,
    Completed = 2,
}
