using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;

namespace VoiceReception.Api.Endpoints;

/// <summary>Çağrı okuma API (CRM / admin) — çağrı geçmişi + transkript + kayıt referansı.
/// X-Internal-Key ile korunur (CRM zaten bu anahtarla /api/tenants çağırır).</summary>
public static class CallReadEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static void MapCallReadApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}/calls")
            .AddEndpointFilter(InternalKey.Filter)
            .AddEndpointFilter(InternalKey.RequireTenantScope);

        group.MapGet("/", ListCalls);
        group.MapGet("/{callLogId:guid}", GetCall);
    }

    /// <summary>Tenant'ın çağrı listesi (en yeni önce). Opsiyonel from/to (UTC) + limit.</summary>
    private static async Task<IResult> ListCalls(
        Guid tenantId, AppDbContext db, CancellationToken ct,
        DateTime? from = null, DateTime? to = null, int limit = DefaultLimit)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        var query = db.CallLogs.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (from is not null) query = query.Where(c => c.StartedAt >= from);
        if (to is not null) query = query.Where(c => c.StartedAt <= to);

        var calls = await query
            .OrderByDescending(c => c.StartedAt)
            .Take(limit)
            .Select(c => new CallSummaryDto(
                c.Id.ToString(), c.Did, c.CustomerPhone, c.StartedAt, c.EndedAt,
                c.DurationSeconds, c.EndReason, c.Outcome, c.ToolCallCount,
                c.RecordingUrl != null))
            .ToListAsync(ct);

        return Results.Ok(calls);
    }

    /// <summary>Tek çağrı + transkript turları. Tenant uyuşmazlığı/yok → 404 (izolasyon).</summary>
    private static async Task<IResult> GetCall(
        Guid tenantId, Guid callLogId, AppDbContext db, CancellationToken ct)
    {
        var call = await db.CallLogs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callLogId && c.TenantId == tenantId, ct);
        if (call is null) return Results.NotFound();

        var turns = await db.ConversationTurns.AsNoTracking()
            .Where(t => t.CallLogId == callLogId)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new CallTurnDto(t.Role, t.Text, t.OccurredAt))
            .ToListAsync(ct);

        return Results.Ok(new CallDetailDto(
            call.Id.ToString(), call.Did, call.CustomerPhone, call.StartedAt, call.EndedAt,
            call.DurationSeconds, call.EndReason, call.Outcome, call.ToolCallCount,
            call.RecordingUrl, turns));
    }
}
