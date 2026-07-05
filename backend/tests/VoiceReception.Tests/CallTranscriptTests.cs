using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoiceReception.Api.Data;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Veri akışı: call_ended ile gelen transkript + analitik CallLog/ConversationTurns'a yazılır.</summary>
public class CallTranscriptTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CallTranscriptTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
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
    public async Task Call_ended_persists_transcript_and_analytics()
    {
        var did = UniqueDid();
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Transkript Test", did })).Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;

        // call_started → CallLog açılır
        (await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started", customerPhone = "+905551112233" })))
            .EnsureSuccessStatusCode();

        // call_ended → transkript turları + analitik
        var ended = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events", new
        {
            tenantId = tid,
            did,
            @event = "call_ended",
            endReason = "normal",
            toolCallCount = 2,
            outcome = "appointment",
            transcript = new[]
            {
                new { role = "user", text = "Merhaba randevu almak istiyorum", occurredAt = (DateTime?)null },
                new { role = "assistant", text = "Tabii, hangi gün?", occurredAt = (DateTime?)null },
            },
        }));
        ended.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.Parse(tid);

        var log = await db.CallLogs.Where(c => c.TenantId == tenantId && c.Did == did)
            .OrderByDescending(c => c.StartedAt).FirstAsync();
        Assert.NotNull(log.EndedAt);
        Assert.Equal("normal", log.EndReason);
        Assert.Equal(2, log.ToolCallCount);
        Assert.Equal("appointment", log.Outcome);
        Assert.NotNull(log.DurationSeconds);

        var turns = await db.ConversationTurns.Where(x => x.CallLogId == log.Id)
            .OrderBy(x => x.OccurredAt).ToListAsync();
        Assert.Equal(2, turns.Count);
        Assert.Contains(turns, x => x.Role == "user" && x.Text.Contains("randevu"));
        Assert.Contains(turns, x => x.Role == "assistant");
    }

    [Fact]
    public async Task Call_started_returns_callLogId()
    {
        var did = UniqueDid();
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "CallId Test", did })).Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;

        var started = await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started" }));
        started.EnsureSuccessStatusCode();
        var body = await started.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("callLogId").GetString(), out _));
    }

    [Fact]
    public async Task CallLogId_attaches_transcript_to_correct_concurrent_call()
    {
        var did = UniqueDid();
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "Concurrent Test", did })).Content.ReadFromJsonAsync<JsonElement>();
        var tid = t.GetProperty("tenantId").GetString()!;

        // Aynı DID'e iki eşzamanlı çağrı (ikisi de açık).
        var startA = await (await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started" }))).Content.ReadFromJsonAsync<JsonElement>();
        var callA = startA.GetProperty("callLogId").GetString()!;
        await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events",
            new { tenantId = tid, did, @event = "call_started" }));  // B, daha yeni

        // A biter, callLogId ile → transkript A'ya yapışmalı (latest-open olsaydı B'ye giderdi).
        (await _client.SendAsync(Keyed(HttpMethod.Post, "/internal/calls/events", new
        {
            tenantId = tid, did, @event = "call_ended", callLogId = callA,
            transcript = new[] { new { role = "user", text = "A çağrısı", occurredAt = (DateTime?)null } },
        }))).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aId = Guid.Parse(callA);

        var logA = await db.CallLogs.FirstAsync(c => c.Id == aId);
        Assert.NotNull(logA.EndedAt);  // A kapandı
        var turnsA = await db.ConversationTurns.Where(x => x.CallLogId == aId).ToListAsync();
        Assert.Single(turnsA);
        Assert.Contains(turnsA, x => x.Text == "A çağrısı");
    }
}
