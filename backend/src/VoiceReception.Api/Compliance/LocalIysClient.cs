using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Compliance;

/// <summary>İYS istemcisinin lokal/test implementasyonu — yerel Consents tablosuna bakar.
///
/// OutboundCampaign tipinde consent kaydı varsa onaylı sayar. Gerçek İYS API entegrasyonu yerine
/// onaylar uygulamaya önceden alınır/içe aktarılır. Üretimde IysApiClient ile değiştirilir.
/// </summary>
public class LocalIysClient : IIysClient
{
    private readonly AppDbContext _db;

    public LocalIysClient(AppDbContext db) => _db = db;

    public Task<bool> HasCampaignConsentAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => _db.Consents.AnyAsync(
            c => c.TenantId == tenantId
                 && c.CustomerPhone == phone
                 && c.Type == ConsentType.OutboundCampaign, ct);
}
