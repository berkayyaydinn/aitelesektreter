using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Tenant config GET/PUT (CRM admin paneli) — X-Internal-Key korumalı.</summary>
public class TenantEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public TenantEndpointsTests(ApiFactory factory)
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

    private async Task<string> CreateTenantAsync(string did, string name = "Panel Test")
    {
        var resp = await _client.PostAsJsonAsync("/api/tenants", new { businessName = name, did });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetString()!;
    }

    [Fact]
    public async Task Get_config_requires_key()
    {
        var resp = await _factory.CreateClient().GetAsync($"/api/tenants/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Put_config_requires_key()
    {
        var resp = await _factory.CreateClient().PutAsJsonAsync($"/api/tenants/{Guid.NewGuid()}",
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
    public async Task Put_persists_prompt_template_and_by_did_returns_it()
    {
        var did = UniqueDid();
        var tid = await CreateTenantAsync(did);

        const string template = "Sen {business_name} için çalışan neşeli bir asistansın. "
            + "Hizmetler: {services}. Saatler: {business_hours}. Önce isim sor.";
        var put = await _client.SendAsync(Keyed(HttpMethod.Put, $"/api/tenants/{tid}", new
        {
            businessName = "Şablonlu İşletme",
            promptTemplate = template,
        }));
        put.EnsureSuccessStatusCode();

        // Admin GET döner.
        var get = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}"));
        var json = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(template, json.GetProperty("promptTemplate").GetString());

        // Voice worker'ın çektiği by-did config de döner.
        var bd = await _client.SendAsync(Keyed(HttpMethod.Get, $"/internal/tenants/by-did/{did}"));
        bd.EnsureSuccessStatusCode();
        var bdJson = await bd.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(template, bdJson.GetProperty("promptTemplate").GetString());
    }

    [Fact]
    public async Task Put_rejects_prompt_template_over_max_length()
    {
        var did = UniqueDid();
        var tid = await CreateTenantAsync(did);

        var put = await _client.SendAsync(Keyed(HttpMethod.Put, $"/api/tenants/{tid}", new
        {
            businessName = "X",
            promptTemplate = new string('a', 4001),
        }));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Create_accepts_prompt_template()
    {
        var did = UniqueDid();
        var resp = await _client.SendAsync(Keyed(HttpMethod.Post, "/api/tenants", new
        {
            businessName = "Doğuştan Şablonlu",
            did,
            promptTemplate = "Sen {business_name} asistanısın.",
        }));
        resp.EnsureSuccessStatusCode();
        var tid = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetString()!;

        var get = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}"));
        var json = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sen {business_name} asistanısın.", json.GetProperty("promptTemplate").GetString());
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
