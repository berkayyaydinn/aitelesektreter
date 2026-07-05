using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Messaging.Sms;

namespace VoiceReception.Api.Reminders;

/// <summary>Arka plan hatırlatma dağıtıcısı — yaklaşan randevu + geciken ödeme SMS'lerini gönderir.
///
/// PeriodicTimer (enjekte TimeProvider) ile periyodik tarar. Her tick'te taze DI scope açar
/// (BackgroundService singleton, AppDbContext/ISmsProvider scoped). Idempotent: yalnız
/// ReminderSentAt == null kayıtları seçer, başarılı gönderimde MessageLog + ReminderSentAt aynı
/// transaction'da yazılır (en-az-bir-kez; başarısız/atlanan tekrar denenir).
///
/// REMINDERS_ENABLED=false ile Program.cs hiç kaydetmez (entegrasyon testleri deterministik).
/// </summary>
public class ReminderDispatcher : BackgroundService
{
    private const int TurkeyOffsetHours = 3; // TR kalıcı UTC+3 (DST yok)

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReminderDispatcher> _logger;

    private readonly int _intervalMinutes;
    private readonly int _appointmentLeadHours;
    private readonly TimeOnly _quietStart;
    private readonly TimeOnly _quietEnd;
    private readonly bool _requireConsent;

    public ReminderDispatcher(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        IConfiguration config,
        ILogger<ReminderDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;

        _intervalMinutes = ParseInt(config["REMINDER_SCAN_INTERVAL_MINUTES"], 5);
        _appointmentLeadHours = ParseInt(config["REMINDER_APPOINTMENT_LEAD_HOURS"], 24);
        _quietStart = ParseTime(config["REMINDER_QUIET_START"], new TimeOnly(9, 0));
        _quietEnd = ParseTime(config["REMINDER_QUIET_END"], new TimeOnly(21, 0));
        _requireConsent = (config["REMINDER_REQUIRE_CONSENT"] ?? "true").ToLowerInvariant() != "false";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_intervalMinutes), _clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hatırlatma dağıtıcı tick hatası");
            }
        }
    }

    /// <summary>Tek tarama: sessiz saat kapısı + randevu + geciken ödeme hatırlatmaları.
    /// Testler bunu doğrudan çağırır (timer'a bağlı değil).</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var nowLocal = nowUtc.AddHours(TurkeyOffsetHours);

        if (!ReminderWindow.IsWithinSendingWindow(TimeOnly.FromDateTime(nowLocal), _quietStart, _quietEnd))
        {
            _logger.LogDebug("Hatırlatma: sessiz saat ({Local:HH:mm}), gönderim atlandı", nowLocal);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsProvider>();

        await DispatchAppointmentRemindersAsync(db, sms, nowUtc, ct);
        await DispatchOverdueInvoiceRemindersAsync(db, sms, nowUtc, ct);
    }

    private async Task DispatchAppointmentRemindersAsync(
        AppDbContext db, ISmsProvider sms, DateTime nowUtc, CancellationToken ct)
    {
        var windowEnd = nowUtc.AddHours(_appointmentLeadHours);
        var due = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Booked
                        && a.ReminderSentAt == null
                        && a.StartUtc > nowUtc
                        && a.StartUtc <= windowEnd)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        var names = await LoadTenantNamesAsync(db, due.Select(a => a.TenantId), ct);

        foreach (var appt in due)
        {
            var local = appt.StartUtc.AddHours(TurkeyOffsetHours);
            var business = names.GetValueOrDefault(appt.TenantId, "");
            var text = $"Sayın {appt.CustomerName}, {local:dd.MM.yyyy} {local:HH\\:mm} randevunuzu hatırlatırız. {business}".TrimEnd();

            await SendOneAsync(db, sms, appt.TenantId, appt.CustomerPhone, text,
                "appointment_reminder", () => appt.ReminderSentAt = nowUtc, ct);
        }
    }

    private async Task DispatchOverdueInvoiceRemindersAsync(
        AppDbContext db, ISmsProvider sms, DateTime nowUtc, CancellationToken ct)
    {
        var due = await db.Invoices
            .Where(i => i.PaymentStatus == PaymentStatus.Unpaid
                        && i.ReminderSentAt == null
                        && i.DueDate != null
                        && i.DueDate < nowUtc
                        && i.CustomerPhone != null)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        var names = await LoadTenantNamesAsync(db, due.Select(i => i.TenantId), ct);

        foreach (var inv in due)
        {
            var business = names.GetValueOrDefault(inv.TenantId, "");
            var text = $"Sayın {inv.CustomerName}, {inv.Amount:0.##} {inv.Currency} tutarındaki ödemenizin vadesi geçmiştir. {business}".TrimEnd();

            await SendOneAsync(db, sms, inv.TenantId, inv.CustomerPhone!, text,
                "invoice_overdue", () => inv.ReminderSentAt = nowUtc, ct);
        }
    }

    /// <summary>Tek hatırlatma gönderimi: rıza kapısı + MessageLog + gönder + (başarıda) işaretle.
    /// Log ve ReminderSentAt aynı SaveChanges'te → idempotent commit.</summary>
    private async Task SendOneAsync(
        AppDbContext db, ISmsProvider sms, Guid tenantId, string phone, string text,
        string template, Action onSuccess, CancellationToken ct)
    {
        if (_requireConsent && !await HasTransactionalConsentAsync(db, tenantId, phone, ct))
        {
            _logger.LogInformation("Hatırlatma atlandı (rıza yok): tenant={Tenant}", tenantId);
            return; // işaretleme yok → rıza gelince tekrar denenir
        }

        var log = new MessageLog
        {
            TenantId = tenantId,
            Channel = sms.Channel,
            ToPhone = phone,
            Template = template,
        };

        var result = await sms.SendAsync(phone, text, ct);
        log.Status = result.Success ? MessageStatus.Sent : MessageStatus.Failed;
        log.ProviderMessageId = result.ProviderMessageId;
        log.Error = result.Error;
        db.MessageLogs.Add(log);

        if (result.Success) onSuccess(); // yalnız başarıda işaretle (hata → null kalır, tekrar)

        await db.SaveChangesAsync(ct);
    }

    private static Task<bool> HasTransactionalConsentAsync(
        AppDbContext db, Guid tenantId, string phone, CancellationToken ct)
        => db.Consents.AnyAsync(
            c => c.TenantId == tenantId
                 && c.CustomerPhone == phone
                 && c.Type == ConsentType.TransactionalSms, ct);

    private static async Task<Dictionary<Guid, string>> LoadTenantNamesAsync(
        AppDbContext db, IEnumerable<Guid> tenantIds, CancellationToken ct)
    {
        var ids = tenantIds.Distinct().ToList();
        return await db.Tenants
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.BusinessName, ct);
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var n) && n > 0 ? n : fallback;

    private static TimeOnly ParseTime(string? value, TimeOnly fallback)
        => TimeOnly.TryParse(value, out var t) ? t : fallback;
}
