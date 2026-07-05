using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Outbound;

/// <summary>Gerçek giden arama — LiveKit SIP (CreateSIPParticipant) ile Netgsm outbound trunk üzerinden.
///
/// Aranan numara LiveKit odasına SIP katılımcısı olarak eklenir; kampanya script'i katılımcı
/// metadata'sında taşınır (agent dispatch rule odayı telesekreter worker'a yönlendirir).
/// OUTBOUND_DIALER=livekit ile seçilir. Kimlik/trunk yoksa arama yapılmadan başarısız döner.
///
/// NOT: SIP davranışı canlı hat gelince doğrulanmalı (docs/netgsm-setup.md). JWT üretimi ve istek
/// gövdesi birim test edilir; gerçek trunk akışı manuel.
/// </summary>
public class LiveKitOutboundDialer : IOutboundDialer
{
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _trunkId;
    private readonly string _httpBase;

    public LiveKitOutboundDialer(HttpClient http, IConfiguration config, TimeProvider clock)
    {
        _http = http;
        _clock = clock;
        _apiKey = config["LIVEKIT_API_KEY"] ?? "";
        _apiSecret = config["LIVEKIT_API_SECRET"] ?? "";
        _trunkId = config["NETGSM_SIP_OUTBOUND_TRUNK_ID"] ?? "";
        _httpBase = ToHttpBase(config["LIVEKIT_URL"] ?? "");
    }

    public async Task<DialResult> PlaceCallAsync(Campaign campaign, CampaignTarget target, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
            return new DialResult(false, "LiveKit kimlik bilgisi yapılandırılmamış");
        if (string.IsNullOrEmpty(_trunkId))
            return new DialResult(false, "NETGSM_SIP_OUTBOUND_TRUNK_ID yapılandırılmamış");
        if (string.IsNullOrEmpty(_httpBase))
            return new DialResult(false, "LIVEKIT_URL yapılandırılmamış");

        var room = $"campaign-{campaign.Id:N}-{target.Id:N}";
        var body = new Dictionary<string, object?>
        {
            ["sipTrunkId"] = _trunkId,
            ["sipCallTo"] = ToE164(target.CustomerPhone),
            ["roomName"] = room,
            ["participantIdentity"] = $"sip-{target.Id:N}",
            ["participantName"] = target.CustomerName,
            // Kampanya script'i metadata'da — worker prompt'u buradan kurar.
            ["participantMetadata"] = JsonSerializer.Serialize(new
            {
                campaignId = campaign.Id,
                tenantId = campaign.TenantId,
                scriptPrompt = campaign.ScriptPrompt,
            }),
        };

        var url = $"{_httpBase}/twirp/livekit.SIP/CreateSIPParticipant";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new("Bearer", BuildAccessToken());

        try
        {
            var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new DialResult(false, $"{(int)resp.StatusCode}: {json}");
            return new DialResult(true, null);
        }
        catch (Exception ex)
        {
            return new DialResult(false, ex.Message);
        }
    }

    /// <summary>LiveKit server API erişim token'ı (HS256 JWT, sip:call + roomAdmin grant).</summary>
    internal string BuildAccessToken()
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = _apiKey,
            ["nbf"] = now,
            ["exp"] = now + 600, // 10 dk
            ["video"] = new { roomAdmin = true, roomCreate = true },
            ["sip"] = new { admin = true, call = true },
        };

        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url(sig)}";
    }

    /// <summary>Numarayı E.164'e çevirir (+90XXXXXXXXXX). SIP call_to standardı.</summary>
    internal static string ToE164(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("90")) return "+" + digits;
        if (digits.StartsWith("0")) return "+90" + digits[1..];
        if (digits.Length == 10) return "+90" + digits;
        return "+" + digits;
    }

    private static string ToHttpBase(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var b = url.Replace("wss://", "https://").Replace("ws://", "http://");
        return b.TrimEnd('/');
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
