using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Netsantral Custom API webhook (/netsantral/inbound) uçtan uca testleri.
/// Bulundu→redirect, bilinmeyen→TTS, yanlış token→401, kapalı→TTS, form+JSON parse.</summary>
public class NetsantralEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public NetsantralEndpointsTests(ApiFactory factory) => _client = factory.CreateKeyedClient();

    private static string UniqueDid() => $"0850{Random.Shared.Next(1_000_000, 9_999_999)}";

    private async Task<string> CreateTenantAsync(string did)
    {
        var resp = await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Netsantral Test", did });
        resp.EnsureSuccessStatusCode();
        var t = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return t.GetProperty("tenantId").GetString()!;
    }

    private async Task<HttpResponseMessage> PostJsonAsync(object body) =>
        await _client.PostAsJsonAsync("/netsantral/inbound", body);

    [Fact]
    public async Task Known_open_did_redirects_to_agent_did()
    {
        var did = UniqueDid();
        await CreateTenantAsync(did); // saat tanımı yok → açık kabul edilir

        // aranan_no baştaki 0 olmadan gelir (Netsantral "850.." formatı) → aday normalizasyonu eşlemeli.
        var resp = await PostJsonAsync(new
        {
            token = ApiFactory.NetsantralToken,
            aranan_no = did.TrimStart('0'),
            arayan_no = "05551112233",
            arama_id = "call-1",
        });

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dynamic", json.GetProperty("result").GetString());
        Assert.Equal(ApiFactory.NetsantralAgentDid, json.GetProperty("redirect").GetString());
    }

    [Fact]
    public async Task Unknown_did_returns_tts_without_redirect()
    {
        var resp = await PostJsonAsync(new
        {
            token = ApiFactory.NetsantralToken,
            aranan_no = "08501234567", // kayıtlı değil
            arama_id = "call-2",
        });

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("1", json.GetProperty("result").GetString());
        Assert.False(json.TryGetProperty("redirect", out _));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("data").GetString()));
    }

    [Fact]
    public async Task Wrong_token_is_unauthorized()
    {
        var resp = await PostJsonAsync(new
        {
            token = "wrong-token",
            aranan_no = UniqueDid(),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Missing_token_is_unauthorized()
    {
        var resp = await PostJsonAsync(new { aranan_no = UniqueDid() });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Closed_all_week_returns_tts()
    {
        var did = UniqueDid();
        var tid = await CreateTenantAsync(did);

        // Tüm hafta kapalı → hangi gün olursa olsun kapalı → TTS.
        var closedWeek = Enumerable.Range(0, 7)
            .Select(d => new { day = d, open = "09:00", close = "18:00", isClosed = true })
            .ToArray();
        var hResp = await _client.PutAsJsonAsync($"/api/tenants/{tid}/hours", closedWeek);
        hResp.EnsureSuccessStatusCode();

        var resp = await PostJsonAsync(new { token = ApiFactory.NetsantralToken, aranan_no = did });
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("1", json.GetProperty("result").GetString());
        Assert.False(json.TryGetProperty("redirect", out _));
    }

    [Fact]
    public async Task Form_post_is_parsed_like_json()
    {
        var did = UniqueDid();
        await CreateTenantAsync(did);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = ApiFactory.NetsantralToken,
            ["aranan_no"] = did,
            ["arayan_no"] = "05551112233",
            ["arama_id"] = "call-form",
        });
        var resp = await _client.PostAsync("/netsantral/inbound", form);

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dynamic", json.GetProperty("result").GetString());
        Assert.Equal(ApiFactory.NetsantralAgentDid, json.GetProperty("redirect").GetString());
    }
}
