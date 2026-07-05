namespace VoiceReception.Api.Endpoints;

// Voice worker <-> backend internal API sözleşmeleri (docs/architecture.md ile eşleşir).

public record AvailabilityRequest(string TenantId, string ServiceId, string Date);
public record AvailabilityResponse(IReadOnlyList<string> Slots);

public record CreateAppointmentRequest(
    string TenantId, string ServiceId, string Date, string Time,
    string CustomerName, string CustomerPhone);

public record CreateOrderRequest(
    string TenantId, string Items, string CustomerName, string CustomerPhone);

// Fatura kesme — yalnızca işletme sahibi (callerPhone == tenant.OwnerPhone).
public record CreateInvoiceRequest(
    string TenantId, string CallerPhone, string CustomerName, decimal Amount,
    string? CustomerPhone, string? Description);

public record CallEventRequest(
    string TenantId, string Did, string Event, string? Consent, string? CustomerPhone,
    string? TranscriptUrl, string? RecordingUrl,
    // call_ended ile gönderilen veri akışı zenginleştirmesi (tümü opsiyonel):
    IReadOnlyList<TranscriptTurnDto>? Transcript = null,
    string? EndReason = null, int? ToolCallCount = null, string? Outcome = null,
    // call_started yanıtındaki callLogId; doğru kayda yapışma için (yoksa latest-open lookup).
    string? CallLogId = null);

/// <summary>call_started yanıtı — worker callLogId'yi saklar, call_ended'de geri gönderir.</summary>
public record CallEventResponse(string CallLogId);

/// <summary>Tek diyalog turu — voice worker call_ended'de tüm konuşmayı toplu gönderir.</summary>
public record TranscriptTurnDto(string Role, string Text, DateTime? OccurredAt);

// ── Çağrı okuma API (CRM / admin) ──────────────────────────────────────────
public record CallSummaryDto(
    string CallLogId, string Did, string? CustomerPhone, DateTime StartedAt, DateTime? EndedAt,
    int? DurationSeconds, string? EndReason, string? Outcome, int ToolCallCount, bool HasRecording);

public record CallDetailDto(
    string CallLogId, string Did, string? CustomerPhone, DateTime StartedAt, DateTime? EndedAt,
    int? DurationSeconds, string? EndReason, string? Outcome, int ToolCallCount,
    string? RecordingUrl, IReadOnlyList<CallTurnDto> Transcript);

public record CallTurnDto(string Role, string Text, DateTime OccurredAt);

// Tenant config (voice worker çağrı başında çeker).
public record TenantConfigResponse(
    string TenantId,
    string BusinessName,
    string? ExtraPrompt,
    string BusinessHoursText,
    IReadOnlyList<TenantServiceDto> Services,
    string? OwnerPhone);   // sahip moduna geçiş için (fatura kesme)

public record TenantServiceDto(string Id, string Name, int DurationMinutes);
