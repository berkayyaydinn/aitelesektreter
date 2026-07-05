using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Gerçek HTTP üzerinden uçtan uca API testleri (voice worker sözleşmesi + onboarding).
/// scripts/smoke_test.py'nin otomatik test karşılığı.</summary>
public class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateKeyedClient();
    }

    private HttpRequestMessage Keyed(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Internal-Key", ApiFactory.Key);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static string UniqueDid() => $"0850{Random.Shared.Next(1_000_000, 9_999_999)}";

    [Fact]
    public async Task Health_returns_ok_with_sqlite()
    {
        var doc = await _client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", doc.GetProperty("status").GetString());
        Assert.Equal("sqlite", doc.GetProperty("db").GetString());
    }

    [Fact]
    public async Task Internal_api_requires_key()
    {
        // Anahtarsız internal çağrı -> 401.
        var resp = await _factory.CreateClient().PostAsJsonAsync("/internal/availability",
            new { tenantId = Guid.NewGuid(), serviceId = Guid.NewGuid(), date = "2026-06-15" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Full_appointment_flow_works_over_http()
    {
        var did = UniqueDid();

        // 1) tenant oluştur
        var tResp = await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "IT Kuafor", did, extraPrompt = "Test." });
        tResp.EnsureSuccessStatusCode();
        var t = await tResp.Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;
        Assert.Equal($"**21*{did}#", t.GetProperty("forwardingInstruction").GetString());

        // 2) hizmet (45 dk)
        var sResp = await _client.PostAsJsonAsync($"/api/tenants/{tid}/services",
            new { name = "Sac kesimi", durationMinutes = 45 });
        sResp.EnsureSuccessStatusCode();
        var sid = (await sResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("serviceId").GetString()!;

        // 3) Pazartesi 09:00-12:00
        var hResp = await _client.PutAsJsonAsync($"/api/tenants/{tid}/hours",
            new[] { new { day = 1, open = "09:00", close = "12:00", isClosed = false } });
        hResp.EnsureSuccessStatusCode();

        // 4) by-did (internal) -> tenant config + 1 hizmet
        var bd = await _client.SendAsync(Keyed(HttpMethod.Get, $"/internal/tenants/by-did/{did}"));
        bd.EnsureSuccessStatusCode();
        var bdJson = await bd.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IT Kuafor", bdJson.GetProperty("businessName").GetString());
        Assert.Equal(1, bdJson.GetProperty("services").GetArrayLength());

        // 5) availability -> 45 dk, 3 saatlik pencere = 4 slot (09:00,09:45,10:30,11:15)
        var av1 = await SlotsAsync(tid, sid);
        Assert.Equal(4, av1);

        // 6) randevu 09:45 -> booked
        var ap = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/appointments",
            new { tenantId = tid, serviceId = sid, date = "2026-06-15", time = "09:45",
                  customerName = "Ali", customerPhone = "+905551112233" }));
        ap.EnsureSuccessStatusCode();
        Assert.Equal("booked", (await ap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // 7) aynı slot -> conflict
        var ap2 = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/appointments",
            new { tenantId = tid, serviceId = sid, date = "2026-06-15", time = "09:45",
                  customerName = "Veli", customerPhone = "+905559998877" }));
        ap2.EnsureSuccessStatusCode();
        Assert.Equal("conflict", (await ap2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // 8) availability daraldı
        Assert.True(await SlotsAsync(tid, sid) < 4);

        // 9) sipariş
        var od = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/orders",
            new { tenantId = tid, items = "2 sampuan", customerName = "Ali", customerPhone = "+905551112233" }));
        od.EnsureSuccessStatusCode();
        Assert.False(string.IsNullOrEmpty(
            (await od.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetString()));

        // 10) çağrı olayı + KVKK consent (call_started)
        var ev = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started",
                  consent = "call_recording_notified", customerPhone = "+905551112233" }));
        ev.EnsureSuccessStatusCode();

        // 11) call_ended — transkript/kayıt URL'leri CallLog'a işlenir
        var ended = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_ended",
                  transcriptUrl = "https://x/t.txt", recordingUrl = "https://x/r.mp3" }));
        ended.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Campaign_flow_runs_with_iys_gate()
    {
        var did = UniqueDid();
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Kampanya Test", did })).Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;

        var cResp = await _client.PostAsJsonAsync($"/api/tenants/{tid}/campaigns",
            new { name = "Yaz", scriptPrompt = "Merhaba, kampanyamız var." });
        cResp.EnsureSuccessStatusCode();
        var cid = (await cResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("campaignId").GetString()!;

        var consented = "+905550000001";
        var noConsent = "+905550000002";
        (await _client.PostAsJsonAsync($"/api/campaigns/{cid}/targets",
            new[] { new { phone = consented, name = "Ali" }, new { phone = noConsent, name = "Veli" } }))
            .EnsureSuccessStatusCode();

        (await _client.PostAsJsonAsync($"/api/campaigns/{cid}/consents",
            new[] { new { phone = consented } })).EnsureSuccessStatusCode();

        // çalıştır — özet yapısı dönmeli (saat penceresine göre called/skipped değişir, yapı sabit)
        var runResp = await _client.PostAsync($"/api/campaigns/{cid}/run", null);
        runResp.EnsureSuccessStatusCode();
        var summary = await runResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summary.TryGetProperty("called", out _));
        Assert.True(summary.TryGetProperty("skipped", out _));

        var statusResp = await _client.GetFromJsonAsync<JsonElement>($"/api/campaigns/{cid}");
        Assert.Equal("Completed", statusResp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invoice_only_owner_can_issue()
    {
        var did = UniqueDid();
        var owner = "+905559990000";
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Fatura Test", did, ownerPhone = owner })).Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;

        // by-did ownerPhone'u döndürür (agent sahip modunu buradan anlar)
        var bd = await _client.SendAsync(Keyed(HttpMethod.Get, $"/internal/tenants/by-did/{did}"));
        var bdJson = await bd.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(owner, bdJson.GetProperty("ownerPhone").GetString());

        // sahip numarasından -> fatura kesilir
        var ok = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/invoices",
            new { tenantId = tid, callerPhone = owner, customerName = "Müşteri", amount = 1500.50,
                  description = "Hizmet bedeli" }));
        ok.EnsureSuccessStatusCode();
        Assert.Equal("Issued", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // başka numaradan -> 403
        var denied = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/invoices",
            new { tenantId = tid, callerPhone = "+905550001111", customerName = "X", amount = 100 }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<int> SlotsAsync(string tid, string sid)
    {
        var resp = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/availability",
            new { tenantId = tid, serviceId = sid, date = "2026-06-15" }));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("slots").GetArrayLength();
    }
}
