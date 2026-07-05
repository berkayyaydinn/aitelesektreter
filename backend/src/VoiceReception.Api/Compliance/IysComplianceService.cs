namespace VoiceReception.Api.Compliance;

/// <summary>Giden arama uygunluk kapısı — İYS onayı + izinli saat penceresi.
///
/// CampaignRunner her aramadan önce bunu çağırır. Onay yoksa veya saat dışıysa arama YAPILMAZ.
/// </summary>
public class IysComplianceService
{
    // İzinli ticari arama saat penceresi. NOT: değerler yasal olarak doğrulanmalı (docs/compliance-iys.md).
    public static readonly TimeOnly AllowedStart = new(9, 0);
    public static readonly TimeOnly AllowedEnd = new(21, 0);

    private readonly IIysClient _iys;

    public IysComplianceService(IIysClient iys) => _iys = iys;

    /// <summary>Belirtilen yerel saatte bu numara aranabilir mi? Olmazsa neden döner.</summary>
    public async Task<CallEligibility> EvaluateAsync(
        Guid tenantId, string phone, DateTime nowLocal, CancellationToken ct = default)
    {
        if (!await _iys.HasCampaignConsentAsync(tenantId, phone, ct))
            return CallEligibility.Block("İYS onayı yok");

        var t = TimeOnly.FromDateTime(nowLocal);
        if (t < AllowedStart || t >= AllowedEnd)
            return CallEligibility.Block($"İzinli saat dışı ({AllowedStart:HH\\:mm}-{AllowedEnd:HH\\:mm})");

        return CallEligibility.Allow();
    }
}

public record CallEligibility(bool Allowed, string? Reason)
{
    public static CallEligibility Allow() => new(true, null);
    public static CallEligibility Block(string reason) => new(false, reason);
}
