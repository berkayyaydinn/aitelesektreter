using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Tenant config GET/PUT (CRM admin paneli) — X-Internal-Key korumalı.</summary>
public class TenantEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public TenantEndpointsTests(ApiFactory factory) => _client = factory.CreateClient();

    private HttpRequestMessage Keyed(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Internal-Key", ApiFactory.Key);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static string UniqueDid() => $"0850{Random.Shared.Next(1_000_000, 9_999_999)}";

    private async Task<string> CreateTenantAsync(string did, string name = "Panel Test")
    {
        var resp = await _client.PostAsJsonAsync("/api/tenants", new { businessName = name, did });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetString()!;
    }

    [Fact]
    public async Task Get_config_requires_key()
    {
        var resp = await _client.GetAsync($"/api/tenants/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Put_config_requires_key()
    {
        var resp = await _client.PutAsJsonAsync($"/api/tenants/{Guid.NewGuid()}",
            new { businessName = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_returns_404_for_missing_tenant()
    {
        var resp = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Put_returns_404_for_missing_tenant()
    {
        var resp = await _client.SendAsync(Keyed(HttpMethod.Put, $"/api/tenants/{Guid.NewGuid()}",
            new { businessName = "X", extraPrompt = "y" }));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_returns_tenant_config()
    {
        var did = UniqueDid();
        var tid = await CreateTenantAsync(did, "Berber Hasan");

        var resp = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}"));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Berber Hasan", json.GetProperty("businessName").GetString());
        Assert.True(json.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Put_updates_config_and_get_reflects_it()
    {
        var did = UniqueDid();
        var tid = await CreateTenantAsync(did);

        var put = await _client.SendAsync(Keyed(HttpMethod.Put, $"/api/tenants/{tid}", new
        {
            businessName = "Yeni Ad",
            extraPrompt = "Kişi başı 50 TL.\nGeciken ödeme tutarı: 200 TL.",
            ownerPhone = "+905551234567",
        }));
        put.EnsureSuccessStatusCode();

        var get = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}"));
        var json = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Yeni Ad", json.GetProperty("businessName").GetString());
        Assert.Contains("Geciken ödeme tutarı: 200 TL", json.GetProperty("extraPrompt").GetString());
        Assert.Equal("+905551234567", json.GetProperty("ownerPhone").GetString());
    }

    [Fact]
    public async Task Put_then_by_did_reflects_extra_prompt_end_to_end()
    {
        var did = UniqueDid();
        var tid = await CreateTenantAsync(did);

        await _client.SendAsync(Keyed(HttpMethod.Put, $"/api/tenants/{tid}", new
        {
            businessName = "Akıllı İşletme",
            extraPrompt = "Özel talimat: önce fiyat söyle.",
        }));

        var bd = await _client.SendAsync(Keyed(HttpMethod.Get, $"/internal/tenants/by-did/{did}"));
        bd.EnsureSuccessStatusCode();
        var json = await bd.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Akıllı İşletme", json.GetProperty("businessName").GetString());
        Assert.Equal("Özel talimat: önce fiyat söyle.", json.GetProperty("extraPrompt").GetString());
    }
}
