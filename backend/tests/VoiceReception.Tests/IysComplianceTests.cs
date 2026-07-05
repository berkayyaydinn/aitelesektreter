using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Compliance;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Outbound;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>İYS onay + izinli saat kapısı ve CampaignRunner — yasal kritik mantık.
/// SQLite in-memory, deterministik saat (nowLocal enjekte).</summary>
public class IysComplianceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly IysComplianceService _compliance;

    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Consented = "+905550000001";
    private const string NoConsent = "+905550000002";

    // 2026-06-15 (Pazartesi) farklı saatler.
    private static DateTime At(int hour) => new(2026, 6, 15, hour, 0, 0, DateTimeKind.Unspecified);

    public IysComplianceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _compliance = new IysComplianceService(new LocalIysClient(_db));

        _db.Tenants.Add(new Tenant { Id = _tenantId, BusinessName = "Test" });
        _db.Consents.Add(new Consent
        {
            TenantId = _tenantId,
            CustomerPhone = Consented,
            Type = ConsentType.OutboundCampaign,
            Source = "iys_import",
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Blocks_when_no_iys_consent()
    {
        var r = await _compliance.EvaluateAsync(_tenantId, NoConsent, At(10), default);
        Assert.False(r.Allowed);
        Assert.Equal("İYS onayı yok", r.Reason);
    }

    [Fact]
    public async Task Blocks_outside_allowed_hours_even_with_consent()
    {
        var r = await _compliance.EvaluateAsync(_tenantId, Consented, At(22), default);
        Assert.False(r.Allowed);
        Assert.Contains("İzinli saat dışı", r.Reason);
    }

    [Fact]
    public async Task Allows_with_consent_inside_hours()
    {
        var r = await _compliance.EvaluateAsync(_tenantId, Consented, At(10), default);
        Assert.True(r.Allowed);
        Assert.Null(r.Reason);
    }

    [Fact]
    public async Task CampaignRunner_calls_consented_skips_unconsented()
    {
        var campaign = new Campaign { TenantId = _tenantId, Name = "Yaz Kampanyası", ScriptPrompt = "Merhaba" };
        _db.Campaigns.Add(campaign);
        _db.CampaignTargets.Add(new CampaignTarget
        { CampaignId = campaign.Id, TenantId = _tenantId, CustomerPhone = Consented, CustomerName = "Ali" });
        _db.CampaignTargets.Add(new CampaignTarget
        { CampaignId = campaign.Id, TenantId = _tenantId, CustomerPhone = NoConsent, CustomerName = "Veli" });
        await _db.SaveChangesAsync();

        var runner = new CampaignRunner(_db, _compliance, new FakeDialer());
        var summary = await runner.RunAsync(campaign.Id, At(10), default);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.Called);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(1, summary.SkipReasons["İYS onayı yok"]);

        var statuses = await _db.CampaignTargets
            .Where(t => t.CampaignId == campaign.Id)
            .ToDictionaryAsync(t => t.CustomerPhone, t => t.Status);
        Assert.Equal(TargetStatus.Completed, statuses[Consented]);
        Assert.Equal(TargetStatus.Skipped, statuses[NoConsent]);
        Assert.Equal(CampaignStatus.Completed, (await _db.Campaigns.FindAsync(campaign.Id))!.Status);
    }

    private sealed class FakeDialer : IOutboundDialer
    {
        public Task<DialResult> PlaceCallAsync(Campaign c, CampaignTarget t, CancellationToken ct = default)
            => Task.FromResult(new DialResult(true, null));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
