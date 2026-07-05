using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Compliance;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Outbound;

namespace VoiceReception.Api.Endpoints;

/// <summary>Giden kampanya yönetimi (tenant-facing). Arama yürütme İYS/saat kapısından geçer.</summary>
public static class CampaignEndpoints
{
    public static void MapCampaignApi(this IEndpointRouteBuilder app)
    {
        var tenant = app.MapGroup("/api/tenants/{tenantId:guid}/campaigns");
        var campaign = app.MapGroup("/api/campaigns/{campaignId:guid}");

        // Kampanya oluştur.
        tenant.MapPost("/", async (Guid tenantId, CreateCampaignBody body, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return Results.NotFound();
            var c = new Campaign { TenantId = tenantId, Name = body.Name, ScriptPrompt = body.ScriptPrompt };
            db.Campaigns.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { campaignId = c.Id });
        });

        // Müşteri listesi içe aktar (arama hedefleri).
        campaign.MapPost("/targets", async (Guid campaignId, IReadOnlyList<TargetBody> body, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
            if (c is null) return Results.NotFound();
            foreach (var t in body)
                db.CampaignTargets.Add(new CampaignTarget
                {
                    CampaignId = campaignId,
                    TenantId = c.TenantId,
                    CustomerPhone = t.Phone,
                    CustomerName = t.Name,
                });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { count = body.Count });
        });

        // İYS onayı kaydet/içe aktar (gerçekte İYS'den senkronize edilir; lokalde test için).
        campaign.MapPost("/consents", async (Guid campaignId, IReadOnlyList<ConsentBody> body, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
            if (c is null) return Results.NotFound();
            foreach (var item in body)
                db.Consents.Add(new Consent
                {
                    TenantId = c.TenantId,
                    CustomerPhone = item.Phone,
                    Type = ConsentType.OutboundCampaign,
                    Source = "iys_import",
                });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { count = body.Count });
        });

        // Kampanyayı yürüt (İSKELE: senkron). İYS/saat kapısı her hedefe uygulanır.
        campaign.MapPost("/run", async (Guid campaignId, CampaignRunner runner, CancellationToken ct) =>
        {
            var summary = await runner.RunAsync(campaignId, TurkeyTime.Now(), ct);
            return summary is null
                ? Results.NotFound()
                : Results.Ok(new { summary.Called, summary.Skipped, summary.Failed, reasons = summary.SkipReasons });
        });

        // Kampanya durumu + hedef dağılımı.
        campaign.MapGet("/", async (Guid campaignId, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
            if (c is null) return Results.NotFound();
            var breakdown = await db.CampaignTargets
                .Where(t => t.CampaignId == campaignId)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToListAsync(ct);
            return Results.Ok(new { c.Id, c.Name, status = c.Status.ToString(), targets = breakdown });
        });
    }

    public record CreateCampaignBody(string Name, string ScriptPrompt);
    public record TargetBody(string Phone, string Name);
    public record ConsentBody(string Phone);
}
