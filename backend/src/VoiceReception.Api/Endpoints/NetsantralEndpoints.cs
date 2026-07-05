using System.Text.Json;
using VoiceReception.Api.Compliance;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Endpoints;

/// <summary>Netsantral Custom (Özel) API webhook'u — çağrı kontrol/karar katmanı.
///
/// Hibrit mimari: Netsantral canlı ses TAŞIMAZ (istek/yanıt webhook). Bu uç yalnız KARAR verir:
///   1) çağrılan numarayı (aranan_no) tenant'a eşle,
///   2) tenant açık ise çağrıyı SIP-trunk'a bağlı iç DID'e (NETSANTRAL_AGENT_DID) yönlendir
///      → oradan LiveKit SIP → voice-agent doğal Türkçe diyaloğu yürütür,
///   3) tenant yok / kapalı ise TTS metni okut (yönlendirme yok).
///
/// Bu uç DB'ye YAZMAZ (salt-okuma). call_started/call_ended + KVKK rızası voice-agent tarafından
/// SIP bacağında işlenir — burada tekrar loglamak çift kayıt üretirdi.
/// </summary>
public static class NetsantralEndpoints
{
    // Netsantral yanıt sözleşmesi: status=success + result. result="dynamic" + redirect => numaraya aktar.
    private const string ClosedMessage =
        "Aradığınız işletme şu anda kapalıdır. Lütfen çalışma saatleri içinde tekrar arayınız.";
    private const string UnknownMessage =
        "Aradığınız numara tanımlı değildir. Lütfen numarayı kontrol edip tekrar deneyiniz.";

    public static void MapNetsantralApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/netsantral/inbound", Inbound);
    }

    private static async Task<IResult> Inbound(
        HttpContext http, AppDbContext db, IConfiguration config, CancellationToken ct)
    {
        var fields = await ReadFieldsAsync(http, ct);

        // Kimlik doğrulama: Netsantral "statik değişken" olarak sabit token gönderir. Fail-closed:
        // token yapılandırılmamışsa veya eşleşmiyorsa 401.
        var expected = config["NETSANTRAL_WEBHOOK_TOKEN"];
        var provided = fields.GetValueOrDefault("token");
        if (string.IsNullOrEmpty(expected) || provided != expected)
            return Results.Unauthorized();

        var agentDid = config["NETSANTRAL_AGENT_DID"];
        if (string.IsNullOrEmpty(agentDid))
            // Yapılandırma eksik: yönlendirilecek iç DID yok → nazik TTS (çağrı düşmez).
            return Tts(UnknownMessage);

        var calledNumber = fields.GetValueOrDefault("aranan_no")
            ?? fields.GetValueOrDefault("santral_no")
            ?? string.Empty;

        var phone = await TenantLookup.FindByDidCandidatesAsync(db, calledNumber, ct);
        if (phone?.Tenant is null)
            return Tts(UnknownMessage);

        if (!IsOpenNow(phone.Tenant, TurkeyTime.Now()))
            return Tts(ClosedMessage);

        // Açık: çağrıyı AI ajanının SIP-trunk DID'ine yönlendir. data boş — karşılama + KVKK anonsu
        // voice-agent tarafında oynatılır.
        return Results.Json(new
        {
            status = "success",
            result = "dynamic",
            data = "",
            redirect = agentDid,
        });
    }

    /// <summary>TTS okutma yanıtı (yönlendirme yok).</summary>
    private static IResult Tts(string message) =>
        Results.Json(new { status = "success", result = "1", data = message });

    /// <summary>Verilen ana göre tenant açık mı? Çalışma saati tanımlı değilse açık kabul edilir
    /// (kısıtlama yok). Bugünün günü kapalı ya da saat dışıysa kapalı.</summary>
    private static bool IsOpenNow(Tenant tenant, DateTime turkeyNow)
    {
        var hours = tenant.BusinessHours;
        if (hours is null || hours.Count == 0) return true; // saat tanımı yok → kısıtlama yok

        var today = hours.FirstOrDefault(h => h.Day == turkeyNow.DayOfWeek);
        if (today is null || today.IsClosed) return false;

        var now = TimeOnly.FromDateTime(turkeyNow);
        return now >= today.OpenTime && now < today.CloseTime;
    }

    /// <summary>İstek gövdesini form (HTTP POST) ya da JSON (JSON POST) olarak okur; alanları
    /// tek bir sözlüğe indirger. Netsantral her iki metodu da destekler.</summary>
    private static async Task<IReadOnlyDictionary<string, string?>> ReadFieldsAsync(
        HttpContext http, CancellationToken ct)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(ct);
            foreach (var kv in form)
                result[kv.Key] = kv.Value.ToString();
            return result;
        }

        // JSON gövdesi (JSON POST). Boş/geçersiz gövde → boş sözlük.
        try
        {
            using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
        }
        catch (JsonException)
        {
            // Ayrıştırılamayan gövde → boş; token eşleşmeyeceği için 401 döner.
        }

        return result;
    }
}
