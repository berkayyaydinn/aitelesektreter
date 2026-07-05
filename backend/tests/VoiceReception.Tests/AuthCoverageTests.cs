using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Onboarding + kampanya uçlarının X-Internal-Key zorunluluğu ve tenant kapsam izolasyonu.
/// (Bu uçlar önceden anahtarsızdı — CRM/dashboard master ya da scoped anahtarla çağırır.)</summary>
public class AuthCoverageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _raw;    // anahtarsız
    private readonly HttpClient _keyed;  // master key

    public AuthCoverageTests(ApiFactory factory)
    {
        _factory = factory;
        _raw = factory.CreateClient();
        _keyed = factory.CreateKeyedClient();
    }

    private static string UniqueDid() => $"0850{Random.Shared.Next(1_000_000, 9_999_999)}";

    [Theory]
    [InlineData("POST", "/api/tenants")]
    [InlineData("POST", "/api/tenants/00000000-0000-0000-0000-000000000001/numbers/08501112233/verify")]
    [InlineData("POST", "/api/tenants/00000000-0000-0000-0000-000000000001/services")]
    [InlineData("PUT", "/api/tenants/00000000-0000-0000-0000-000000000001/hours")]
    [InlineData("POST", "/api/tenants/00000000-0000-0000-0000-000000000001/campaigns")]
    [InlineData("POST", "/api/campaigns/00000000-0000-0000-0000-000000000002/targets")]
    [InlineData("POST", "/api/campaigns/00000000-0000-0000-0000-000000000002/consents")]
    [InlineData("POST", "/api/campaigns/00000000-0000-0000-0000-000000000002/run")]
    [InlineData("GET", "/api/campaigns/00000000-0000-0000-0000-000000000002")]
    public async Task Endpoints_require_internal_key(string method, string url)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        // Dizi bekleyen uçlara boş dizi gönder — binding 400'e düşmesin, auth filtresi test edilsin.
        var expectsArray = url.EndsWith("/hours") || url.EndsWith("/targets") || url.EndsWith("/consents");
        if (method != "GET")
            req.Content = JsonContent.Create<object>(expectsArray ? Array.Empty<object>() : new { });
        var resp = await _raw.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Scoped_key_cannot_touch_other_tenants_campaigns()
    {
        // Master key ile iki tenant + diğerine ait kampanya kur.
        var ownDid = UniqueDid();
        var otherDid = UniqueDid();
        var own = await CreateTenantAsync(ownDid, "Kendi İşletme");
        var other = await CreateTenantAsync(otherDid, "Başka İşletme");

        var cResp = await _keyed.PostAsJsonAsync($"/api/tenants/{other}/campaigns",
            new { name = "Gizli", scriptPrompt = "..." });
        cResp.EnsureSuccessStatusCode();
        var cid = (await cResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("campaignId").GetString()!;

        const string scopedKey = "scoped-campaign-key";
        var scoped = _factory.WithWebHostBuilder(b =>
            b.UseSetting("INTERNAL_TENANT_KEYS", $"{own}:{scopedKey}")).CreateClient();
        scoped.DefaultRequestHeaders.Add("X-Internal-Key", scopedKey);

        // Başka tenant altında kampanya oluşturma → 403.
        var createOther = await scoped.PostAsJsonAsync($"/api/tenants/{other}/campaigns",
            new { name = "İzinsiz", scriptPrompt = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, createOther.StatusCode);

        // Başka tenant'ın kampanyasına hedef ekleme / okuma → 403.
        var targets = await scoped.PostAsJsonAsync($"/api/campaigns/{cid}/targets",
            new[] { new { phone = "+905551112233", name = "Ali" } });
        Assert.Equal(HttpStatusCode.Forbidden, targets.StatusCode);

        var status = await scoped.GetAsync($"/api/campaigns/{cid}");
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);

        // Kendi tenant'ında kampanya oluşturma → 200.
        var createOwn = await scoped.PostAsJsonAsync($"/api/tenants/{own}/campaigns",
            new { name = "Kendi", scriptPrompt = "y" });
        Assert.Equal(HttpStatusCode.OK, createOwn.StatusCode);
    }

    [Fact]
    public async Task Scoped_key_cannot_create_tenants()
    {
        const string scopedKey = "scoped-no-create";
        var scoped = _factory.WithWebHostBuilder(b =>
            b.UseSetting("INTERNAL_TENANT_KEYS", $"{Guid.NewGuid()}:{scopedKey}")).CreateClient();
        scoped.DefaultRequestHeaders.Add("X-Internal-Key", scopedKey);

        var resp = await scoped.PostAsJsonAsync("/api/tenants",
            new { businessName = "İzinsiz Tenant", did = UniqueDid() });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private async Task<string> CreateTenantAsync(string did, string name)
    {
        var resp = await _keyed.PostAsJsonAsync("/api/tenants", new { businessName = name, did });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetString()!;
    }
}
