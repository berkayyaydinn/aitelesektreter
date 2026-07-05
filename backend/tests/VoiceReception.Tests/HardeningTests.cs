using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Sertleştirme: soft-delete + scoped internal key (tenant izolasyonu).</summary>
public class HardeningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public HardeningTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Keyed(HttpMethod method, string url, string key, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Internal-Key", key);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static string UniqueDid() => $"0850{Random.Shared.Next(1_000_000, 9_999_999)}";

    [Fact]
    public async Task Soft_deleted_tenant_disappears_from_by_did_and_get()
    {
        var did = UniqueDid();
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Silinecek", did })).Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;

        // Silmeden önce: by-did bulur.
        var before = await _client.SendAsync(Keyed(HttpMethod.Get, $"/internal/tenants/by-did/{did}", ApiFactory.Key));
        before.EnsureSuccessStatusCode();

        // Soft-delete.
        var del = await _client.SendAsync(Keyed(HttpMethod.Delete, $"/api/tenants/{tid}", ApiFactory.Key));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Sonra: by-did 404, admin GET 404 (geçmiş DB'de durur ama gizli).
        var byDid = await _client.SendAsync(Keyed(HttpMethod.Get, $"/internal/tenants/by-did/{did}", ApiFactory.Key));
        Assert.Equal(HttpStatusCode.NotFound, byDid.StatusCode);
        var get = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}", ApiFactory.Key));
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Scoped_key_can_access_only_its_own_tenant()
    {
        var ownTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        const string scopedKey = "scoped-key-abc";

        var scopedClient = _factory.WithWebHostBuilder(b =>
            b.UseSetting("INTERNAL_TENANT_KEYS", $"{ownTenant}:{scopedKey}")).CreateClient();

        // Kendi tenant'ının çağrı listesi → 200 (tenant kaydı olmasa da liste boş döner).
        var own = await scopedClient.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{ownTenant}/calls", scopedKey));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        // Başka tenant → 403 (scope ihlali).
        var other = await scopedClient.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{otherTenant}/calls", scopedKey));
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);

        // Master key → her tenant'a erişir.
        var master = await scopedClient.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{otherTenant}/calls", ApiFactory.Key));
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);

        // Geçersiz anahtar → 401.
        var bad = await scopedClient.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{ownTenant}/calls", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }
}
