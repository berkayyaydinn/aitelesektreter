using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Retention;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>RetentionSweeper (KVKK saklama/imha) iş kuralları — SQLite in-memory + TestTimeProvider.
///
/// Varsayılan süreler: çağrı logu 365g, ses kaydı URL 180g, mesaj logu 365g, PII 365g.
/// Consent (ispat yükü) ve Invoice (vergi mevzuatı 5 yıl) hiçbir adımda silinmez/anonimleşmez.
/// </summary>
public class RetentionSweeperTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _provider;

    private readonly Guid _tenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    public RetentionSweeperTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.Tenants.Add(new Tenant { Id = _tenantId, BusinessName = "Test İşletme" });
        db.SaveChanges();
    }

    // ─────────────────────────── çağrı logu + transkript ───────────────────────────

    [Fact]
    public async Task Expired_call_log_and_turns_are_deleted_recent_kept()
    {
        var oldId = SeedCallLog(Now.AddDays(-400), turns: 2);
        var newId = SeedCallLog(Now.AddDays(-10), turns: 1);

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(db.CallLogs.Find(oldId));
        Assert.NotNull(db.CallLogs.Find(newId));
        Assert.Equal(0, db.ConversationTurns.Count(t => t.CallLogId == oldId));
        Assert.Equal(1, db.ConversationTurns.Count(t => t.CallLogId == newId));
    }

    [Fact]
    public async Task Recording_url_cleared_after_180_days_but_call_log_kept_until_365()
    {
        var midId = SeedCallLog(Now.AddDays(-200), recordingUrl: "http://minio/rec.ogg");
        var newId = SeedCallLog(Now.AddDays(-10), recordingUrl: "http://minio/new.ogg");

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mid = db.CallLogs.Find(midId);
        Assert.NotNull(mid);                       // 365g dolmadı → satır kalır
        Assert.Null(mid!.RecordingUrl);            // 180g doldu → ses referansı imha
        Assert.Equal("http://minio/new.ogg", db.CallLogs.Find(newId)!.RecordingUrl);
    }

    // ─────────────────────────── mesaj logu ───────────────────────────

    [Fact]
    public async Task Expired_message_log_deleted_recent_kept()
    {
        SeedMessageLog(Now.AddDays(-400));
        SeedMessageLog(Now.AddDays(-10));

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.MessageLogs.Count());
    }

    // ─────────────────────────── randevu / sipariş PII ───────────────────────────

    [Fact]
    public async Task Expired_appointment_anonymized_but_row_kept()
    {
        var oldId = SeedAppointment(Now.AddDays(-400));
        var newId = SeedAppointment(Now.AddDays(-10));

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = db.Appointments.IgnoreQueryFilters().Single(a => a.Id == oldId);
        Assert.Equal(RetentionDefaults.Anonymized, old.CustomerName);
        Assert.Equal(string.Empty, old.CustomerPhone);
        var recent = db.Appointments.Single(a => a.Id == newId);
        Assert.Equal("Ali", recent.CustomerName);
    }

    [Fact]
    public async Task Soft_deleted_expired_appointment_is_still_anonymized()
    {
        var oldId = SeedAppointment(Now.AddDays(-400), isDeleted: true);

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = db.Appointments.IgnoreQueryFilters().Single(a => a.Id == oldId);
        Assert.Equal(RetentionDefaults.Anonymized, old.CustomerName);
    }

    [Fact]
    public async Task Expired_order_anonymized_but_row_kept()
    {
        var oldId = SeedOrder(Now.AddDays(-400));

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = db.Orders.IgnoreQueryFilters().Single(o => o.Id == oldId);
        Assert.Equal(RetentionDefaults.Anonymized, old.CustomerName);
        Assert.Equal(string.Empty, old.CustomerPhone);
    }

    // ─────────────────────────── dokunulmayanlar ───────────────────────────

    [Fact]
    public async Task Consent_and_invoice_are_never_touched()
    {
        SeedConsent(Now.AddDays(-2000));
        SeedInvoice(Now.AddDays(-2000));

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.Consents.Count());
        var invoice = db.Invoices.Single();
        Assert.Equal("Veli", invoice.CustomerName); // anonimleşmedi
    }

    // ─────────────────────────── denetim kaydı ───────────────────────────

    [Fact]
    public async Task Audit_row_written_with_counts()
    {
        SeedCallLog(Now.AddDays(-400), turns: 2, recordingUrl: "http://minio/x.ogg");
        SeedCallLog(Now.AddDays(-200), recordingUrl: "http://minio/y.ogg");
        SeedMessageLog(Now.AddDays(-400));
        SeedAppointment(Now.AddDays(-400));
        SeedOrder(Now.AddDays(-400));

        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = db.RetentionRuns.Single();
        Assert.Equal(Now, run.RanAt);
        Assert.Equal(1, run.CallLogsDeleted);
        Assert.Equal(2, run.TurnsDeleted);
        Assert.Equal(2, run.RecordingsCleared);    // 400g + 200g olan ikisi de 180g'i aştı
        Assert.Equal(1, run.MessageLogsDeleted);
        Assert.Equal(1, run.AppointmentsAnonymized);
        Assert.Equal(1, run.OrdersAnonymized);
    }

    [Fact]
    public async Task Second_run_is_idempotent()
    {
        SeedCallLog(Now.AddDays(-400), turns: 2);
        SeedAppointment(Now.AddDays(-400));

        await RunOnce(Now);
        await RunOnce(Now);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runs = db.RetentionRuns.ToList();
        Assert.Equal(2, runs.Count);
        Assert.Equal(1, runs.Sum(r => r.CallLogsDeleted));          // ikinci turda 0
        Assert.Equal(1, runs.Sum(r => r.AppointmentsAnonymized));   // ikinci turda 0
    }

    // ─────────────────────────── yardımcılar ───────────────────────────

    private async Task RunOnce(DateTime nowUtc)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var sweeper = new RetentionSweeper(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new TestTimeProvider(nowUtc),
            config,
            NullLogger<RetentionSweeper>.Instance);

        await sweeper.RunOnceAsync(default);
    }

    private Guid SeedCallLog(DateTime startedAt, int turns = 0, string? recordingUrl = null)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = new CallLog
        {
            TenantId = _tenantId,
            Did = "08501112233",
            CustomerPhone = "+905551112233",
            StartedAt = startedAt,
            RecordingUrl = recordingUrl,
        };
        db.CallLogs.Add(log);
        for (var i = 0; i < turns; i++)
        {
            db.ConversationTurns.Add(new ConversationTurn
            {
                CallLogId = log.Id,
                Role = "user",
                Text = $"tur {i}",
                OccurredAt = startedAt,
            });
        }
        db.SaveChanges();
        return log.Id;
    }

    private void SeedMessageLog(DateTime createdAt)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.MessageLogs.Add(new MessageLog
        {
            TenantId = _tenantId,
            ToPhone = "+905551112233",
            Template = "t",
            CreatedAt = createdAt,
        });
        db.SaveChanges();
    }

    private Guid SeedAppointment(DateTime createdAt, bool isDeleted = false)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appt = new Appointment
        {
            TenantId = _tenantId,
            ServiceId = Guid.NewGuid(),
            StartUtc = createdAt.AddDays(1),
            EndUtc = createdAt.AddDays(1).AddHours(1),
            CustomerName = "Ali",
            CustomerPhone = "+905551112233",
            CreatedAt = createdAt,
            IsDeleted = isDeleted,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();
        return appt.Id;
    }

    private Guid SeedOrder(DateTime createdAt)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order
        {
            TenantId = _tenantId,
            Items = "1 kutu",
            CustomerName = "Ali",
            CustomerPhone = "+905551112233",
            CreatedAt = createdAt,
        };
        db.Orders.Add(order);
        db.SaveChanges();
        return order.Id;
    }

    private void SeedConsent(DateTime createdAt)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Consents.Add(new Consent
        {
            TenantId = _tenantId,
            CustomerPhone = "+905551112233",
            Type = ConsentType.CallRecording,
            Source = "test",
            CreatedAt = createdAt,
        });
        db.SaveChanges();
    }

    private void SeedInvoice(DateTime createdAt)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Invoices.Add(new Invoice
        {
            TenantId = _tenantId,
            CustomerName = "Veli",
            CustomerPhone = "+905551112233",
            Amount = 500m,
            CreatedAt = createdAt,
        });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }
}
