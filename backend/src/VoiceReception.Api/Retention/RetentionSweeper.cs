using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Retention;

/// <summary>KVKK saklama/imha arka plan servisi (docs/legal/saklama-imha-politikasi.md §5).
///
/// PeriodicTimer (enjekte TimeProvider) ile periyodik tarar; her tick taze DI scope açar.
/// Adımlar: (1) süresi dolan ses kaydı URL'lerini null'la, (2) süresi dolan CallLog +
/// ConversationTurn'leri sil, (3) süresi dolan MessageLog'ları sil, (4) süresi dolan
/// Appointment/Order PII'sini anonimleştir (satır kalır), (5) RetentionRun denetim satırı yaz.
///
/// Bilinçli olarak DOKUNULMAYANLAR:
///  - Consent: rıza ispat yükü — hukuki savunma için saklanır.
///  - Invoice: vergi mevzuatı (VUK) 5 yıl saklama zorunluluğu.
///
/// Ses dosyasının fiziksel imhası MinIO bucket lifecycle (ILM) kuralıyla yapılır; burada yalnız
/// DB referansı temizlenir. RETENTION_ENABLED=false ile Program.cs hiç kaydetmez.
/// </summary>
public class RetentionSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<RetentionSweeper> _logger;

    private readonly int _scanIntervalHours;
    private readonly int _callLogDays;
    private readonly int _recordingDays;
    private readonly int _messageLogDays;
    private readonly int _piiDays;

    public RetentionSweeper(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        IConfiguration config,
        ILogger<RetentionSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;

        _scanIntervalHours = ParseInt(config["RETENTION_SCAN_INTERVAL_HOURS"], 24);
        _callLogDays = ParseInt(config["RETENTION_CALL_LOG_DAYS"], 365);
        _recordingDays = ParseInt(config["RETENTION_RECORDING_DAYS"], 180);
        _messageLogDays = ParseInt(config["RETENTION_MESSAGE_LOG_DAYS"], 365);
        _piiDays = ParseInt(config["RETENTION_PII_DAYS"], 365);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_scanIntervalHours), _clock);
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
                _logger.LogError(ex, "Retention tarama hatası");
            }
        }
    }

    /// <summary>Tek imha taraması. Testler bunu doğrudan çağırır (timer'a bağlı değil).
    /// Tüm adımlar tek SaveChanges'te commit edilir — denetim satırı ile imha atomiktir.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = new RetentionRun { RanAt = nowUtc };
        run.RecordingsCleared = await ClearExpiredRecordingUrlsAsync(db, nowUtc, ct);
        (run.CallLogsDeleted, run.TurnsDeleted) = await DeleteExpiredCallLogsAsync(db, nowUtc, ct);
        run.MessageLogsDeleted = await DeleteExpiredMessageLogsAsync(db, nowUtc, ct);
        (run.AppointmentsAnonymized, run.OrdersAnonymized) = await AnonymizeExpiredPiiAsync(db, nowUtc, ct);

        db.RetentionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        if (run.CallLogsDeleted + run.TurnsDeleted + run.RecordingsCleared + run.MessageLogsDeleted
            + run.AppointmentsAnonymized + run.OrdersAnonymized > 0)
        {
            _logger.LogInformation(
                "Retention: callLog={CallLogs} tur={Turns} kayıt={Recordings} mesaj={Messages} randevu={Appointments} sipariş={Orders}",
                run.CallLogsDeleted, run.TurnsDeleted, run.RecordingsCleared,
                run.MessageLogsDeleted, run.AppointmentsAnonymized, run.OrdersAnonymized);
        }
    }

    /// <summary>Ses kaydı referansını imha et (dosyayı MinIO ILM siler). CallLog satırı kalır.</summary>
    private async Task<int> ClearExpiredRecordingUrlsAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-_recordingDays);
        var expired = await db.CallLogs
            .Where(c => c.RecordingUrl != null && c.StartedAt < cutoff)
            .ToListAsync(ct);
        foreach (var log in expired) log.RecordingUrl = null;
        return expired.Count;
    }

    /// <summary>Çağrı logu + transkripti tamamen sil (en uzun saklama süresi dolunca).</summary>
    private async Task<(int Logs, int Turns)> DeleteExpiredCallLogsAsync(
        AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-_callLogDays);
        var expired = await db.CallLogs.Where(c => c.StartedAt < cutoff).ToListAsync(ct);
        if (expired.Count == 0) return (0, 0);

        var ids = expired.Select(c => c.Id).ToList();
        var turns = await db.ConversationTurns.Where(t => ids.Contains(t.CallLogId)).ToListAsync(ct);
        db.ConversationTurns.RemoveRange(turns);
        db.CallLogs.RemoveRange(expired);
        return (expired.Count, turns.Count);
    }

    private async Task<int> DeleteExpiredMessageLogsAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-_messageLogDays);
        var expired = await db.MessageLogs.Where(m => m.CreatedAt < cutoff).ToListAsync(ct);
        db.MessageLogs.RemoveRange(expired);
        return expired.Count;
    }

    /// <summary>Randevu/sipariş PII'sini anonimleştir — satır kalır (istatistik), kişisel veri gider.
    /// IgnoreQueryFilters: soft-delete edilmiş kayıtların PII'si de imha edilmeli.</summary>
    private async Task<(int Appointments, int Orders)> AnonymizeExpiredPiiAsync(
        AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-_piiDays);

        var appointments = await db.Appointments.IgnoreQueryFilters()
            .Where(a => a.CreatedAt < cutoff && a.CustomerName != RetentionDefaults.Anonymized)
            .ToListAsync(ct);
        foreach (var a in appointments)
        {
            a.CustomerName = RetentionDefaults.Anonymized;
            a.CustomerPhone = string.Empty;
        }

        var orders = await db.Orders.IgnoreQueryFilters()
            .Where(o => o.CreatedAt < cutoff && o.CustomerName != RetentionDefaults.Anonymized)
            .ToListAsync(ct);
        foreach (var o in orders)
        {
            o.CustomerName = RetentionDefaults.Anonymized;
            o.CustomerPhone = string.Empty;
        }

        return (appointments.Count, orders.Count);
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var n) && n > 0 ? n : fallback;
}
