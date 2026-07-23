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
        // CRM/dashboard uçları: master ya da scoped anahtar zorunlu (InternalKey).
        var tenant = app.MapGroup("/api/tenants/{tenantId:guid}/campaigns")
            .AddEndpointFilter(InternalKey.Filter)
            .AddEndpointFilter(InternalKey.RequireTenantScope);
        var campaign = app.MapGroup("/api/campaigns/{campaignId:guid}")
            .AddEndpointFilter(InternalKey.Filter)
            .AddEndpointFilter(RequireCampaignScope);

        // Tenant'ın kampanya listesi (süper-admin paneli / CRM).
        tenant.MapGet("/", async (Guid tenantId, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return Results.NotFound();
            var campaigns = await db.Campaigns.AsNoTracking()
                .Where(c => c.TenantId == tenantId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    campaignId = c.Id,
                    c.Name,
                    status = c.Status.ToString(),
                    targetCount = db.CampaignTargets.Count(t => t.CampaignId == c.Id),
                    c.CreatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(campaigns);
        });

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

    /// <summary>Scoped anahtar, yalnız kendi tenant'ının kampanyasına dokunabilir (403).
    /// Master anahtarla no-op; kampanya yoksa handler'ın 404'üne bırakılır.</summary>
    private static async ValueTask<object?> RequireCampaignScope(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (ctx.HttpContext.Items.TryGetValue(InternalKey.ScopedTenantItem, out var raw) && raw is Guid scopedTenant)
        {
            var routeCampaign = ctx.HttpContext.Request.RouteValues["campaignId"]?.ToString();
            if (Guid.TryParse(routeCampaign, out var campaignId))
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var ownerTenant = await db.Campaigns
                    .Where(c => c.Id == campaignId)
                    .Select(c => (Guid?)c.TenantId)
                    .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
                if (ownerTenant is not null && ownerTenant != scopedTenant)
                    return Results.Json(new { error = "Bu kampanyaya erişim yetkisi yok" }, statusCode: 403);
            }
        }
        return await next(ctx);
    }

    public record CreateCampaignBody(string Name, string ScriptPrompt);
    public record TargetBody(string Phone, string Name);
    public record ConsentBody(string Phone);
}
