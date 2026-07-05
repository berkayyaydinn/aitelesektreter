namespace VoiceReception.Api.Messaging.Sms;

/// <summary>Netgsm HTTP SMS API ile düz metin SMS gönderimi.
///
/// MVP: tek hesap (NETGSM_USERCODE/PASSWORD) + onaylı başlık (NETGSM_MSGHEADER) env'den.
/// Netgsm yanıtı JSON değil, kısa metin "&lt;kod&gt; &lt;jobid&gt;" — savunmacı parse edilir.
/// SMS_PROVIDER=netgsm ile seçilir.
/// </summary>
public class NetgsmSmsProvider : ISmsProvider
{
    private const string SendUrl = "https://api.netgsm.com.tr/sms/send/get/";

    private readonly HttpClient _http;
    private readonly string _usercode;
    private readonly string _password;
    private readonly string _msgHeader;

    public string Channel => "sms";

    public NetgsmSmsProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _usercode = config["NETGSM_USERCODE"] ?? "";
        _password = config["NETGSM_PASSWORD"] ?? "";
        _msgHeader = config["NETGSM_MSGHEADER"] ?? "";
    }

    public async Task<SmsResult> SendAsync(string toPhone, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_usercode) || string.IsNullOrEmpty(_password))
            return new SmsResult(false, null, "Netgsm yapılandırılmamış");

        var gsmno = NormalizePhone(toPhone);
        var query =
            $"?usercode={Uri.EscapeDataString(_usercode)}" +
            $"&password={Uri.EscapeDataString(_password)}" +
            $"&gsmno={Uri.EscapeDataString(gsmno)}" +
            $"&message={Uri.EscapeDataString(text)}" +
            $"&msgheader={Uri.EscapeDataString(_msgHeader)}" +
            "&dil=TR";

        try
        {
            var resp = await _http.GetAsync(SendUrl + query, ct);
            var body = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode)
                return new SmsResult(false, null, $"HTTP {(int)resp.StatusCode}: {body}");
            return ParseResponse(body);
        }
        catch (Exception ex)
        {
            return new SmsResult(false, null, ex.Message);
        }
    }

    /// <summary>Netgsm yanıt gövdesini sonuca çevirir. Gövde "&lt;kod&gt;" ya da "&lt;kod&gt; &lt;jobid&gt;".
    /// Sadece açık başarı kodunda (00/01/02) başarı döner — belirsizde gönderim başarısız sayılır
    /// (sessiz kayıp yerine tekrar denenir).</summary>
    public static SmsResult ParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new SmsResult(false, null, "Netgsm boş yanıt");

        var parts = body.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var code = parts[0];

        if (code is "00" or "01" or "02")
        {
            var jobId = parts.Length > 1 ? parts[1] : null;
            return new SmsResult(true, jobId, null);
        }

        var reason = ErrorReasons.TryGetValue(code, out var r) ? r : "bilinmeyen hata";
        return new SmsResult(false, null, $"Netgsm {code}: {reason}");
    }

    /// <summary>Numarayı Netgsm formatına çevirir: 90XXXXXXXXXX (artı işareti yok).
    /// "+90...", "0...", "90...", boşluk/tire içeren girişleri normalize eder.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("90"))
            return digits;
        if (digits.StartsWith("0"))
            return "90" + digits[1..];
        if (digits.Length == 10) // 5XXXXXXXXX
            return "90" + digits;
        return digits;
    }

    // Netgsm hata kodları → okunur sebep (loglar için). Resmi dokümandan.
    private static readonly Dictionary<string, string> ErrorReasons = new()
    {
        ["20"] = "mesaj metni hatalı / çok uzun",
        ["30"] = "geçersiz kimlik veya API erişimi/IP kısıtı",
        ["40"] = "msgheader (gönderici başlığı) tanımsız",
        ["50"] = "İYS kontrollü hesap — alıcı İYS kaydı yok",
        ["51"] = "İYS marka bilgisi eksik",
        ["70"] = "hatalı parametre",
        ["80"] = "gönderim limiti aşıldı",
        ["85"] = "mükerrer gönderim limiti",
    };
}
