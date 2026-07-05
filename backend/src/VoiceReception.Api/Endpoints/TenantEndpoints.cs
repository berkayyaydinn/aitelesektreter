using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Endpoints;

/// <summary>Tenant onboarding / yönetim API (mevcut uygulamanın dashboard'ı tüketir).
/// MVP: minimal — tenant oluştur, DID ata, yönlendirme talimatı döndür.</summary>
public static class TenantEndpoints
{
    /// <summary>CRM şablon promptu üst sınırı — LLM bağlamını ve TTS maliyetini korur.</summary>
    public const int PromptTemplateMaxLength = 4000;

    private static IResult? ValidatePromptTemplate(string? template) =>
        template?.Length > PromptTemplateMaxLength
            ? Results.BadRequest(new { error = $"promptTemplate en fazla {PromptTemplateMaxLength} karakter olabilir" })
            : null;

    public static void MapTenantApi(this IEndpointRouteBuilder app)
    {
        // Onboarding da anahtar ister: master (CRM) ya da kendi tenant'ına kısıtlı scoped anahtar.
        // POST / route'unda {tenantId} yok → scoped anahtar tenant oluşturamaz (403), sadece master.
        var group = app.MapGroup("/api/tenants")
            .AddEndpointFilter(InternalKey.Filter)
            .AddEndpointFilter(InternalKey.RequireTenantScope);

        // Tenant oluştur + DID tahsis et + yönlendirme talimatı döndür.
        group.MapPost("/", async (CreateTenantBody body, AppDbContext db, CancellationToken ct) =>
        {
            if (ValidatePromptTemplate(body.PromptTemplate) is { } invalid) return invalid;
            var tenant = new Tenant
            {
                BusinessName = body.BusinessName,
                ExtraPrompt = body.ExtraPrompt,
                PromptTemplate = body.PromptTemplate,
                OwnerPhone = body.OwnerPhone,
            };
            db.Tenants.Add(tenant);

            var phone = new PhoneNumber { TenantId = tenant.Id, Did = body.Did };
            db.PhoneNumbers.Add(phone);

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                tenantId = tenant.Id,
                did = phone.Did,
                forwardingInstruction = $"**21*{phone.Did}#",
                note = "Telefonunuzda bu kodu çevirip arama tuşuna basın. Sonra test araması yapın.",
            });
        });

        // Yönlendirme doğrulandı (test çağrısı sonrası).
        group.MapPost("/{tenantId:guid}/numbers/{did}/verify",
            async (Guid tenantId, string did, AppDbContext db, CancellationToken ct) =>
        {
            var phone = await db.PhoneNumbers
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Did == did, ct);
            if (phone is null) return Results.NotFound();
            phone.ForwardingStatus = ForwardingStatus.Active;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { status = "active" });
        });

        // Hizmet ekle (randevu/sipariş kalemi).
        group.MapPost("/{tenantId:guid}/services",
            async (Guid tenantId, AddServiceBody body, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return Results.NotFound();
            var service = new Service
            {
                TenantId = tenantId,
                Name = body.Name,
                DurationMinutes = body.DurationMinutes <= 0 ? 30 : body.DurationMinutes,
            };
            db.Services.Add(service);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { serviceId = service.Id });
        });

        // Haftalık çalışma saatlerini topluca ayarla (mevcutları değiştirir).
        group.MapPut("/{tenantId:guid}/hours",
            async (Guid tenantId, IReadOnlyList<HourBody> body, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return Results.NotFound();

            var existing = await db.BusinessHours.Where(h => h.TenantId == tenantId).ToListAsync(ct);
            db.BusinessHours.RemoveRange(existing);

            foreach (var h in body)
            {
                db.BusinessHours.Add(new BusinessHour
                {
                    TenantId = tenantId,
                    Day = (DayOfWeek)h.Day,
                    OpenTime = TimeOnly.Parse(h.Open),
                    CloseTime = TimeOnly.Parse(h.Close),
                    IsClosed = h.IsClosed,
                });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { count = body.Count });
        });

        // ── Telesekreter config okuma/güncelleme (CRM admin paneli, X-Internal-Key korumalı) ──
        var admin = app.MapGroup("/api/tenants")
            .AddEndpointFilter(InternalKey.Filter)
            .AddEndpointFilter(InternalKey.RequireTenantScope);

        // Tenant config oku (panel ön-doldurma + teyit).
        admin.MapGet("/{tenantId:guid}", async (Guid tenantId, AppDbContext db, CancellationToken ct) =>
        {
            var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
            if (t is null) return Results.NotFound();
            return Results.Ok(new
            {
                tenantId = t.Id,
                businessName = t.BusinessName,
                extraPrompt = t.ExtraPrompt,
                promptTemplate = t.PromptTemplate,
                ownerPhone = t.OwnerPhone,
                isActive = t.IsActive,
            });
        });

        // Tenant config güncelle (firma adı, konuşma metni/ek talimat, sahip telefonu, aktiflik).
        admin.MapPut("/{tenantId:guid}",
            async (Guid tenantId, UpdateTenantBody body, AppDbContext db, CancellationToken ct) =>
        {
            if (ValidatePromptTemplate(body.PromptTemplate) is { } invalid) return invalid;
            var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
            if (t is null) return Results.NotFound();

            t.BusinessName = body.BusinessName;
            t.ExtraPrompt = body.ExtraPrompt;
            t.PromptTemplate = body.PromptTemplate;
            t.OwnerPhone = body.OwnerPhone;
            if (body.IsActive.HasValue) t.IsActive = body.IsActive.Value;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { tenantId = t.Id });
        });

        // Tenant'ı soft-delete et (geçmiş korunur; tüm sorgulardan gizlenir, DID routing durur).
        admin.MapDelete("/{tenantId:guid}", async (Guid tenantId, AppDbContext db, CancellationToken ct) =>
        {
            var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
            if (t is null) return Results.NotFound();
            t.IsDeleted = true;
            t.IsActive = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public record CreateTenantBody(
        string BusinessName, string Did, string? ExtraPrompt, string? OwnerPhone,
        string? PromptTemplate = null);
    public record AddServiceBody(string Name, int DurationMinutes);
    public record HourBody(int Day, string Open, string Close, bool IsClosed);
    public record UpdateTenantBody(
        string BusinessName, string? ExtraPrompt, string? OwnerPhone, bool? IsActive,
        string? PromptTemplate = null);
}
