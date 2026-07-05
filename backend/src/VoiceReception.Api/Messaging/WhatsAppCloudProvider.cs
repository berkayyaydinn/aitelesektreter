using System.Net.Http.Json;

namespace VoiceReception.Api.Messaging;

/// <summary>Meta WhatsApp Cloud API ile template mesaj gönderimi.
///
/// MVP: tek WABA (pilot tenant) token'ı env'den. Çok-kiracılı Embedded Signup ile per-tenant
/// token'a geçiş bu sınıfın arkasında izole — çağıran taraf IMessagingProvider görür.
/// </summary>
public class WhatsAppCloudProvider : IMessagingProvider
{
    private readonly HttpClient _http;
    private readonly string _phoneNumberId;
    private readonly string _accessToken;

    public string Channel => "whatsapp";

    public WhatsAppCloudProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _phoneNumberId = config["WHATSAPP_PHONE_NUMBER_ID"] ?? "";
        _accessToken = config["WHATSAPP_ACCESS_TOKEN"] ?? "";
    }

    public async Task<MessageResult> SendTemplateAsync(
        string toPhone,
        string templateName,
        IReadOnlyList<string> parameters,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_accessToken))
            return new MessageResult(false, null, "WhatsApp token yapılandırılmamış");

        var url = $"https://graph.facebook.com/v20.0/{_phoneNumberId}/messages";
        var body = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = "tr" },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = parameters.Select(p => new { type = "text", text = p }).ToArray(),
                    },
                },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", _accessToken);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new MessageResult(false, null, $"{(int)resp.StatusCode}: {json}");
            return new MessageResult(true, ExtractMessageId(json), null);
        }
        catch (Exception ex)
        {
            return new MessageResult(false, null, ex.Message);
        }
    }

    private static string? ExtractMessageId(string json)
    {
        // {"messages":[{"id":"wamid..."}]}
        var marker = "\"id\":\"";
        var i = json.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        i += marker.Length;
        var end = json.IndexOf('"', i);
        return end > i ? json[i..end] : null;
    }
}
