namespace VoiceReception.Api.Compliance;

/// <summary>İYS (İleti Yönetim Sistemi) onay sorgu soyutlaması (swappable).
///
/// Giden ticari arama TR'de İYS onayı ister. Lokal/test: <see cref="LocalIysClient"/> yerel
/// Consents tablosuna bakar. Üretim: gerçek İYS API'sine sorgu atan implementasyon eklenir.
/// </summary>
public interface IIysClient
{
    /// <summary>Bu numara için tenant'a ait giden kampanya onayı var mı?</summary>
    Task<bool> HasCampaignConsentAsync(Guid tenantId, string phone, CancellationToken ct = default);
}
