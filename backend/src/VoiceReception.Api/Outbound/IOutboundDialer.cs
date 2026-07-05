using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Outbound;

/// <summary>Giden arama yerleştirme soyutlaması (swappable).
///
/// Dry-run: <see cref="ConsoleOutboundDialer"/> (log'a yazar, gerçek arama yok). Üretim: LiveKit SIP
/// outbound (createSIPParticipant) ile gerçek arama + kampanya prompt'lu agent dispatch.
/// </summary>
public interface IOutboundDialer
{
    Task<DialResult> PlaceCallAsync(Campaign campaign, CampaignTarget target, CancellationToken ct = default);
}

public record DialResult(bool Success, string? Reason);
