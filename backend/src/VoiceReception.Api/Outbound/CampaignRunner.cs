using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Compliance;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Outbound;

/// <summary>Kampanyayı yürütür: her hedef için İYS/saat kapısı → izinliyse ara, değilse Skipped + neden.
///
/// İSKELE: senkron çalışır (küçük listeler için). Üretimde arka plan kuyruğu + tenant bazlı hız sınırı
/// + yeniden deneme gerekir (bkz. docs/compliance-iys.md).
/// </summary>
public class CampaignRunner
{
    private readonly AppDbContext _db;
    private readonly IysComplianceService _compliance;
    private readonly IOutboundDialer _dialer;

    public CampaignRunner(AppDbContext db, IysComplianceService compliance, IOutboundDialer dialer)
    {
        _db = db;
        _compliance = compliance;
        _dialer = dialer;
    }

    public async Task<RunSummary?> RunAsync(Guid campaignId, DateTime nowLocal, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null) return null;

        campaign.Status = CampaignStatus.Running;

        var targets = await _db.CampaignTargets
            .Where(t => t.CampaignId == campaignId && t.Status == TargetStatus.Pending)
            .ToListAsync(ct);

        var summary = new RunSummary();
        foreach (var target in targets)
        {
            var eligibility = await _compliance.EvaluateAsync(
                campaign.TenantId, target.CustomerPhone, nowLocal, ct);

            if (!eligibility.Allowed)
            {
                target.Status = TargetStatus.Skipped;
                target.SkipReason = eligibility.Reason;
                summary.Skipped++;
                summary.AddReason(eligibility.Reason!);
                continue;
            }

            target.LastAttemptUtc = DateTime.UtcNow;
            var result = await _dialer.PlaceCallAsync(campaign, target, ct);
            if (result.Success)
            {
                target.Status = TargetStatus.Completed;
                summary.Called++;
            }
            else
            {
                target.Status = TargetStatus.Failed;
                target.SkipReason = result.Reason;
                summary.Failed++;
            }
        }

        campaign.Status = CampaignStatus.Completed;
        await _db.SaveChangesAsync(ct);
        return summary;
    }
}

public class RunSummary
{
    public int Called { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public Dictionary<string, int> SkipReasons { get; } = new();

    public void AddReason(string reason)
        => SkipReasons[reason] = SkipReasons.GetValueOrDefault(reason) + 1;
}
