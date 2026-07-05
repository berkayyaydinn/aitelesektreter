using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Messaging.Sms;
using VoiceReception.Api.Reminders;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>ReminderDispatcher iş kuralları — SQLite in-memory + TestTimeProvider + sahte SMS.
///
/// Varsayılan "şimdi" = 2026-06-15 09:00 UTC → TR yerel 12:00 (gönderim penceresi içinde).
/// Çoğu test rızayı kapatır (REMINDER_REQUIRE_CONSENT=false); rıza kapısı ayrı test edilir.
/// </summary>
public class ReminderDispatcherTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _provider;
    private readonly FakeSmsProvider _sms = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc); // yerel 12:00

    public ReminderDispatcherTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
        services.AddSingleton<ISmsProvider>(_sms);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.Tenants.Add(new Tenant { Id = _tenantId, BusinessName = "Test İşletme" });
        db.SaveChanges();
    }

    // ─────────────────────────── randevu ───────────────────────────

    [Fact]
    public async Task Appointment_within_lead_window_is_reminded()
    {
        SeedAppointment(Now.AddHours(12));
        await RunOnce(Now);

        Assert.Single(_sms.Sent);
        Assert.NotNull(SingleAppointment().ReminderSentAt);
        Assert.Equal(1, CountMessageLogs(MessageStatus.Sent));
    }

    [Fact]
    public async Task Appointment_outside_window_not_reminded()
    {
        SeedAppointment(Now.AddHours(48)); // lead 24 dışında
        await RunOnce(Now);
        Assert.Empty(_sms.Sent);
        Assert.Null(SingleAppointment().ReminderSentAt);
    }

    [Fact]
    public async Task Cancelled_appointment_not_reminded()
    {
        SeedAppointment(Now.AddHours(12), status: AppointmentStatus.Cancelled);
        await RunOnce(Now);
        Assert.Empty(_sms.Sent);
    }

    [Fact]
    public async Task Past_appointment_not_reminded()
    {
        SeedAppointment(Now.AddHours(-1));
        await RunOnce(Now);
        Assert.Empty(_sms.Sent);
    }

    [Fact]
    public async Task Idempotent_no_double_send_across_ticks()
    {
        SeedAppointment(Now.AddHours(12));
        await RunOnce(Now);
        await RunOnce(Now);

        Assert.Single(_sms.Sent);                       // ikinci tick tekrar göndermez
        Assert.Equal(1, CountMessageLogs(MessageStatus.Sent));
    }

    [Fact]
    public async Task Failed_send_does_not_mark_sent()
    {
        _sms.Fail = true;
        SeedAppointment(Now.AddHours(12));
        await RunOnce(Now);

        Assert.Single(_sms.Sent);
        Assert.Null(SingleAppointment().ReminderSentAt);  // hata → null kalır (tekrar denenir)
        Assert.Equal(1, CountMessageLogs(MessageStatus.Failed));
    }

    [Fact]
    public async Task Quiet_hours_skip_does_not_send()
    {
        // 19:30 UTC → TR yerel 22:30 (pencere dışı).
        var night = new DateTime(2026, 6, 15, 19, 30, 0, DateTimeKind.Utc);
        SeedAppointment(night.AddHours(12));
        await RunOnce(night);

        Assert.Empty(_sms.Sent);
        Assert.Null(SingleAppointment().ReminderSentAt);
    }

    // ─────────────────────────── rıza kapısı ───────────────────────────

    [Fact]
    public async Task Consent_required_and_missing_skips_without_marking()
    {
        SeedAppointment(Now.AddHours(12), phone: "+905559998877");
        await RunOnce(Now, requireConsent: true); // rıza kaydı yok

        Assert.Empty(_sms.Sent);
        Assert.Null(SingleAppointment().ReminderSentAt); // tekrar denenebilir
    }

    [Fact]
    public async Task Consent_required_and_present_sends()
    {
        SeedAppointment(Now.AddHours(12), phone: "+905559998877");
        SeedConsent("+905559998877");
        await RunOnce(Now, requireConsent: true);

        Assert.Single(_sms.Sent);
        Assert.NotNull(SingleAppointment().ReminderSentAt);
    }

    // ─────────────────────────── geciken ödeme ───────────────────────────

    [Fact]
    public async Task Overdue_unpaid_invoice_is_reminded()
    {
        SeedInvoice(dueUtc: Now.AddHours(-2), status: PaymentStatus.Unpaid, phone: "+905551112233");
        await RunOnce(Now);

        Assert.Single(_sms.Sent);
        Assert.NotNull(SingleInvoice().ReminderSentAt);
    }

    [Fact]
    public async Task Paid_invoice_not_reminded()
    {
        SeedInvoice(dueUtc: Now.AddHours(-2), status: PaymentStatus.Paid, phone: "+905551112233");
        await RunOnce(Now);
        Assert.Empty(_sms.Sent);
    }

    [Fact]
    public async Task Not_yet_due_invoice_not_reminded()
    {
        SeedInvoice(dueUtc: Now.AddHours(48), status: PaymentStatus.Unpaid, phone: "+905551112233");
        await RunOnce(Now);
        Assert.Empty(_sms.Sent);
    }

    [Fact]
    public async Task Invoice_without_phone_is_skipped()
    {
        SeedInvoice(dueUtc: Now.AddHours(-2), status: PaymentStatus.Unpaid, phone: null);
        await RunOnce(Now);
        Assert.Empty(_sms.Sent);
    }

    // ─────────────────────────── yardımcılar ───────────────────────────

    private async Task RunOnce(DateTime nowUtc, bool requireConsent = false)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["REMINDER_APPOINTMENT_LEAD_HOURS"] = "24",
            ["REMINDER_REQUIRE_CONSENT"] = requireConsent ? "true" : "false",
        }).Build();

        var dispatcher = new ReminderDispatcher(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new TestTimeProvider(nowUtc),
            config,
            NullLogger<ReminderDispatcher>.Instance);

        await dispatcher.RunOnceAsync(default);
    }

    private void SeedAppointment(DateTime startUtc, AppointmentStatus status = AppointmentStatus.Booked,
        string phone = "+905551112233")
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Appointments.Add(new Appointment
        {
            TenantId = _tenantId,
            ServiceId = Guid.NewGuid(),
            StartUtc = startUtc,
            EndUtc = startUtc.AddHours(1),
            CustomerName = "Ali",
            CustomerPhone = phone,
            Status = status,
        });
        db.SaveChanges();
    }

    private void SeedInvoice(DateTime dueUtc, PaymentStatus status, string? phone)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Invoices.Add(new Invoice
        {
            TenantId = _tenantId,
            CustomerName = "Veli",
            CustomerPhone = phone,
            Amount = 500m,
            DueDate = dueUtc,
            PaymentStatus = status,
        });
        db.SaveChanges();
    }

    private void SeedConsent(string phone)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Consents.Add(new Consent
        {
            TenantId = _tenantId,
            CustomerPhone = phone,
            Type = ConsentType.TransactionalSms,
            Source = "test",
        });
        db.SaveChanges();
    }

    private Appointment SingleAppointment()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Appointments.AsNoTracking().Single();
    }

    private Invoice SingleInvoice()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Invoices.AsNoTracking().Single();
    }

    private int CountMessageLogs(MessageStatus status)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.MessageLogs.Count(m => m.Status == status);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    private sealed class FakeSmsProvider : ISmsProvider
    {
        public bool Fail;
        public List<(string Phone, string Text)> Sent { get; } = new();

        public string Channel => "fake-sms";

        public Task<SmsResult> SendAsync(string toPhone, string text, CancellationToken ct = default)
        {
            Sent.Add((toPhone, text));
            return Task.FromResult(Fail
                ? new SmsResult(false, null, "boom")
                : new SmsResult(true, "fake-id", null));
        }
    }
}
