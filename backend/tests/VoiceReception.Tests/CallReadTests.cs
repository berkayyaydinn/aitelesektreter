using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Çağrı okuma API — liste + detay + auth + tenant izolasyonu.</summary>
public class CallReadTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CallReadTests(ApiFactory factory)
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

    private async Task<string> CreateTenant(string did)
    {
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Read Test", did })).Content.ReadFromJsonAsync<JsonElement>();
        return t.GetProperty("tenantId").GetString()!;
    }

    [Fact]
    public async Task List_requires_internal_key()
    {
        var resp = await _factory.CreateClient().GetAsync($"/api/tenants/{Guid.NewGuid()}/calls");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task List_and_detail_return_call_with_transcript()
    {
        var did = UniqueDid();
        var tid = await CreateTenant(did);

        var started = await (await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started", customerPhone = "+905551112233" })))
            .Content.ReadFromJsonAsync<JsonElement>();
        var callId = started.GetProperty("callLogId").GetString()!;

        (await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events", new
        {
            tenantId = tid, did, @event = "call_ended", callLogId = callId,
            endReason = "normal", toolCallCount = 1, outcome = "order",
            recordingUrl = "s3://bucket/recordings/x.ogg",
            transcript = new[] { new { role = "user", text = "selam", occurredAt = (DateTime?)null } },
        }))).EnsureSuccessStatusCode();

        // Liste
        var list = await (await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}/calls")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetArrayLength());
        var item = list[0];
        Assert.Equal(did, item.GetProperty("did").GetString());
        Assert.Equal("order", item.GetProperty("outcome").GetString());
        Assert.True(item.GetProperty("hasRecording").GetBoolean());

        // Detay
        var detail = await (await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{tid}/calls/{callId}")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("s3://bucket/recordings/x.ogg", detail.GetProperty("recordingUrl").GetString());
        var transcript = detail.GetProperty("transcript");
        Assert.Equal(1, transcript.GetArrayLength());
        Assert.Equal("selam", transcript[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Detail_returns_404_for_other_tenant()
    {
        var did = UniqueDid();
        var tid = await CreateTenant(did);
        var started = await (await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started" }))).Content.ReadFromJsonAsync<JsonElement>();
        var callId = started.GetProperty("callLogId").GetString()!;

        // Başka tenant ID ile aynı callLogId → 404 (izolasyon).
        var resp = await _client.SendAsync(Keyed(HttpMethod.Get, $"/api/tenants/{Guid.NewGuid()}/calls/{callId}"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
