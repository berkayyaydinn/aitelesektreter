using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Retention;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>DSAR (KVKK m.11) silme talebi — POST /api/tenants/{id}/dsar/erase.
///
/// Bir telefon numarasının PII izini tenant kapsamında anonimleştirir: CallLog telefonu +
/// ConversationTurn'ler (transkript) + Appointment/Order ad-telefon + MessageLog.ToPhone.
/// Consent (ispat yükü) ve Invoice (vergi mevzuatı) korunur. Idempotent.
/// </summary>
public class DsarTests : IClassFixture<ApiFactory>
{
    private const string Phone = "+905551112233";

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public DsarTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateKeyedClient();
    }

    private static string UniqueDid() => $"0850{Random.Shared.Next(1_000_000, 9_999_999)}";

    private async Task<Guid> CreateTenant()
    {
        var t = await (await _client.PostAsJsonAsync("/api/tenants",
            new { businessName = "DSAR Test", did = UniqueDid() })).Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(t.GetProperty("tenantId").GetString()!);
    }

    /// <summary>Telefonun geçtiği tüm tablolara birer kayıt eker (DB'ye doğrudan).</summary>
    private Guid SeedAllTables(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var call = new CallLog { TenantId = tenantId, Did = "0850", CustomerPhone = Phone };
        db.CallLogs.Add(call);
        db.ConversationTurns.Add(new ConversationTurn { CallLogId = call.Id, Role = "user", Text = "adım Ali" });
        db.Appointments.Add(new Appointment
        {
            TenantId = tenantId, ServiceId = Guid.NewGuid(),
            StartUtc = DateTime.UtcNow.AddDays(1), EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
            CustomerName = "Ali", CustomerPhone = Phone,
        });
        db.Orders.Add(new Order { TenantId = tenantId, Items = "x", CustomerName = "Ali", CustomerPhone = Phone });
        db.MessageLogs.Add(new MessageLog { TenantId = tenantId, ToPhone = Phone, Template = "t" });
        db.Consents.Add(new Consent { TenantId = tenantId, CustomerPhone = Phone, Type = ConsentType.CallRecording, Source = "test" });
        db.Invoices.Add(new Invoice { TenantId = tenantId, CustomerName = "Ali", CustomerPhone = Phone, Amount = 100m });
        db.SaveChanges();
        return call.Id;
    }

    [Fact]
    public async Task Erase_anonymizes_pii_and_deletes_transcript()
    {
        var tenantId = await CreateTenant();
        var callId = SeedAllTables(tenantId);

        var resp = await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/dsar/erase", new { phone = Phone });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("callLogsAnonymized").GetInt32());
        Assert.Equal(1, body.GetProperty("turnsDeleted").GetInt32());
        Assert.Equal(1, body.GetProperty("appointmentsAnonymized").GetInt32());
        Assert.Equal(1, body.GetProperty("ordersAnonymized").GetInt32());
        Assert.Equal(1, body.GetProperty("messageLogsAnonymized").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(RetentionDefaults.Anonymized, db.CallLogs.Single(c => c.Id == callId).CustomerPhone);
        Assert.Equal(0, db.ConversationTurns.Count(t => t.CallLogId == callId));
        var appt = db.Appointments.IgnoreQueryFilters().Single(a => a.TenantId == tenantId);
        Assert.Equal(RetentionDefaults.Anonymized, appt.CustomerName);
        Assert.Equal(string.Empty, appt.CustomerPhone);
        var order = db.Orders.IgnoreQueryFilters().Single(o => o.TenantId == tenantId);
        Assert.Equal(RetentionDefaults.Anonymized, order.CustomerName);
        Assert.Equal(RetentionDefaults.Anonymized, db.MessageLogs.Single(m => m.TenantId == tenantId).ToPhone);

        // Korunanlar: Consent (ispat), Invoice (vergi).
        Assert.Equal(Phone, db.Consents.Single(c => c.TenantId == tenantId).CustomerPhone);
        Assert.Equal(Phone, db.Invoices.Single(i => i.TenantId == tenantId).CustomerPhone);
    }

    [Fact]
    public async Task Erase_is_idempotent_second_call_returns_zeros()
    {
        var tenantId = await CreateTenant();
        SeedAllTables(tenantId);

        (await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/dsar/erase", new { phone = Phone }))
            .EnsureSuccessStatusCode();
        var second = await (await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/dsar/erase",
            new { phone = Phone })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, second.GetProperty("callLogsAnonymized").GetInt32());
        Assert.Equal(0, second.GetProperty("turnsDeleted").GetInt32());
        Assert.Equal(0, second.GetProperty("appointmentsAnonymized").GetInt32());
        Assert.Equal(0, second.GetProperty("ordersAnonymized").GetInt32());
        Assert.Equal(0, second.GetProperty("messageLogsAnonymized").GetInt32());
    }

    [Fact]
    public async Task Erase_does_not_touch_other_tenants_data()
    {
        var tenantA = await CreateTenant();
        var tenantB = await CreateTenant();
        SeedAllTables(tenantA);
        SeedAllTables(tenantB);

        (await _client.PostAsJsonAsync($"/api/tenants/{tenantA}/dsar/erase", new { phone = Phone }))
            .EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(Phone, db.CallLogs.Single(c => c.TenantId == tenantB).CustomerPhone);
        Assert.Equal("Ali", db.Appointments.Single(a => a.TenantId == tenantB).CustomerName);
    }

    [Fact]
    public async Task Erase_requires_internal_key()
    {
        var resp = await _factory.CreateClient()
            .PostAsJsonAsync($"/api/tenants/{Guid.NewGuid()}/dsar/erase", new { phone = Phone });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Erase_with_empty_phone_returns_400()
    {
        var tenantId = await CreateTenant();
        var resp = await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/dsar/erase", new { phone = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Erase_unknown_tenant_returns_404()
    {
        var resp = await _client.PostAsJsonAsync($"/api/tenants/{Guid.NewGuid()}/dsar/erase", new { phone = Phone });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
