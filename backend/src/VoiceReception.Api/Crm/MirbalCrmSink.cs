using System.Net.Http.Json;
using System.Text.Json;

namespace VoiceReception.Api.Crm;

/// <summary>
/// Mirbal CRM HTTP istemcisi. Olayları <c>/api/telesekreter</c> uçlarına aynalar; kimlik
/// <c>X-Api-Key</c> header'ı ile (HttpClient üzerinde Program.cs'te set edilir).
///
/// Tüm metotlar best-effort: ağ/HTTP hatası loglanır ve yutulur, telesekreter akışını bozmaz.
/// </summary>
public sealed class MirbalCrmSink : ICrmSink
{
    private readonly HttpClient _http;
    private readonly ILogger<MirbalCrmSink> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public MirbalCrmSink(HttpClient http, ILogger<MirbalCrmSink> logger)
    {
        _http = http;
        _logger = logger;
    }

    public bool Enabled => true;

    public async Task<int?> FindCustomerIdByPhoneAsync(string? phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        try
        {
            var resp = await _http.GetAsync($"/api/telesekreter/musteri/ara?q={Uri.EscapeDataString(phone)}", ct);
            if (!resp.IsSuccessStatusCode) return null;

            var sonuc = await resp.Content.ReadFromJsonAsync<List<CrmCustomerMatch>>(JsonOpts, ct);
            return sonuc is { Count: > 0 } ? sonuc[0].Id : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CRM müşteri arama başarısız (phone={Phone})", phone);
            return null;
        }
    }

    public Task MirrorAppointmentAsync(CrmAppointment appointment, CancellationToken ct = default) =>
        PostBestEffortAsync("/api/telesekreter/randevu", new
        {
            baslik = appointment.Title,
            aciklama = appointment.Description,
            baslangicZamani = appointment.StartUtc,
            bitisZamani = appointment.EndUtc,
            musteriId = appointment.CustomerId,
        }, "randevu", ct);

    public Task MirrorLeadAsync(CrmLead lead, CancellationToken ct = default) =>
        PostBestEffortAsync("/api/telesekreter/lead", new
        {
            ad = lead.Name,
            soyAd = lead.Surname,
            telefon = lead.Phone,
            email = lead.Email,
            firma = lead.Company,
            notlar = lead.Notes,
        }, "lead", ct);

    public Task MirrorActivityAsync(CrmActivity activity, CancellationToken ct = default) =>
        PostBestEffortAsync("/api/telesekreter/aktivite", new
        {
            tip = activity.Type,
            baslik = activity.Title,
            aciklama = activity.Description,
            tarih = activity.Date,
            musteriId = activity.CustomerId,
            leadId = activity.LeadId,
        }, "aktivite", ct);

    private async Task PostBestEffortAsync(string path, object payload, string kind, CancellationToken ct)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(path, payload, JsonOpts, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("CRM {Kind} aynalama HTTP {Status}", kind, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CRM {Kind} aynalama başarısız", kind);
        }
    }

    private sealed record CrmCustomerMatch(int Id);
}
