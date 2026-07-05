namespace VoiceReception.Api.Crm;

/// <summary>CRM bağlantı ayarları (.env: CRM_PROVIDER / CRM_BASE_URL / CRM_API_KEY).</summary>
public sealed class CrmOptions
{
    /// <summary>none = kapalı (varsayılan) | mirbal = Mirbal CRM /api/crm.</summary>
    public string Provider { get; init; } = "none";

    /// <summary>CRM kök URL'i, ör. https://crm.firma.com.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>CRM tarafı X-Api-Key (AppSettings["CrmApiKey"] ile eşleşmeli).</summary>
    public string? ApiKey { get; init; }
}

/// <summary>CRM'e aynalanacak randevu (Mirbal POST /api/crm/randevu ile eşleşir).</summary>
public sealed record CrmAppointment(
    string? Title,
    string? Description,
    DateTime StartUtc,
    DateTime EndUtc,
    int? CustomerId);

/// <summary>CRM'e aynalanacak lead (Mirbal POST /api/crm/lead ile eşleşir).</summary>
public sealed record CrmLead(
    string Name,
    string? Surname,
    string? Phone,
    string? Email,
    string? Company,
    string? Notes);

/// <summary>CRM'e aynalanacak aktivite (Mirbal POST /api/crm/aktivite ile eşleşir).
/// <paramref name="Type"/> AktiviteTip enum'una karşılık gelir (0=Arama).</summary>
public sealed record CrmActivity(
    int Type,
    string? Title,
    string? Description,
    DateTime? Date,
    int? CustomerId,
    int? LeadId);
