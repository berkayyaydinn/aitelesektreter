namespace VoiceReception.Api.Domain;

/// <summary>Çağrı meta verisi + transkript referansı (webhook ile dolar).</summary>
public class CallLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Did { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    public string? TranscriptUrl { get; set; }
    public string? RecordingUrl { get; set; }

    // ── Çağrı analitiği (call_ended olayında dolar) ──────────────────────────
    /// <summary>Çağrı süresi (saniye). EndedAt - StartedAt'ten türetilir.</summary>
    public int? DurationSeconds { get; set; }
    /// <summary>Bitiş sebebi: "normal" | "error" | "timeout" vb.</summary>
    public string? EndReason { get; set; }
    /// <summary>Çağrı boyunca yapılan tool çağrısı sayısı.</summary>
    public int ToolCallCount { get; set; }
    /// <summary>Çağrı sonucu: "appointment" | "order" | "invoice" | "none" | "error".</summary>
    public string? Outcome { get; set; }
}
