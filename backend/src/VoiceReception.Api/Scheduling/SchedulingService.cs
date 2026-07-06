using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Scheduling;

/// <summary>Randevu uygunluk + çakışmasız oluşturma. İş kuralları tek yerde (voice worker delege eder).</summary>
public class SchedulingService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IBookingLock _bookingLock;

    public SchedulingService(AppDbContext db, TimeProvider clock, IBookingLock bookingLock)
    {
        _db = db;
        _clock = clock;
        _bookingLock = bookingLock;
    }

    /// <summary>Çalışma saatleri içinde, dolu/geçmiş slotları çıkararak uygun slotları üretir.</summary>
    public async Task<IReadOnlyList<string>> GetAvailabilityAsync(
        Guid tenantId, Guid serviceId, DateOnly date, CancellationToken ct)
    {
        // Pasif hizmet slot üretmez (rezervasyona kapalı).
        var service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.TenantId == tenantId && s.IsActive, ct);
        if (service is null) return Array.Empty<string>();

        var hours = await _db.BusinessHours
            .FirstOrDefaultAsync(h => h.TenantId == tenantId && h.Day == date.DayOfWeek, ct);
        if (hours is null || hours.IsClosed) return Array.Empty<string>();

        var dayStartUtc = date.ToDateTime(hours.OpenTime, DateTimeKind.Utc);
        var dayEndUtc = date.ToDateTime(hours.CloseTime, DateTimeKind.Utc);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var taken = await _db.Appointments
            .Where(a => a.TenantId == tenantId
                        && a.Status == AppointmentStatus.Booked
                        && a.StartUtc < dayEndUtc && a.EndUtc > dayStartUtc)
            .Select(a => new { a.StartUtc, a.EndUtc })
            .ToListAsync(ct);

        var step = TimeSpan.FromMinutes(service.DurationMinutes);
        var slots = new List<string>();
        for (var t = dayStartUtc; t + step <= dayEndUtc; t += step)
        {
            if (t <= nowUtc) continue; // geçmiş slot önerilmez
            var slotEnd = t + step;
            var overlaps = taken.Any(a => a.StartUtc < slotEnd && a.EndUtc > t);
            if (!overlaps) slots.Add(t.ToString("HH:mm"));
        }
        return slots;
    }

    /// <summary>
    /// Randevu oluşturur. Hizmet/çalışma saati/geçmiş kontrolleri + çakışma. Son anda dolarsa
    /// (yarış) Conflict döner: app-seviye AnyAsync hızlı yolu, Postgres exclusion constraint atomik garanti.
    /// </summary>
    public async Task<BookingResult> CreateAppointmentAsync(
        Guid tenantId, Guid serviceId, DateOnly date, TimeOnly time,
        string customerName, string customerPhone, CancellationToken ct)
    {
        var service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.TenantId == tenantId && s.IsActive, ct);
        if (service is null) return new BookingResult(BookingOutcome.ServiceUnavailable, null);

        var startUtc = date.ToDateTime(time, DateTimeKind.Utc);
        var endUtc = startUtc.AddMinutes(service.DurationMinutes);

        if (startUtc <= _clock.GetUtcNow().UtcDateTime)
            return new BookingResult(BookingOutcome.InPast, null);

        // Çalışma saati zorlaması: availability ne öneriyorsa create de onu kabul etmeli.
        var hours = await _db.BusinessHours
            .FirstOrDefaultAsync(h => h.TenantId == tenantId && h.Day == date.DayOfWeek, ct);
        if (hours is null || hours.IsClosed)
            return new BookingResult(BookingOutcome.OutsideBusinessHours, null);

        var openUtc = date.ToDateTime(hours.OpenTime, DateTimeKind.Utc);
        var closeUtc = date.ToDateTime(hours.CloseTime, DateTimeKind.Utc);
        if (startUtc < openUtc || endUtc > closeUtc)
            return new BookingResult(BookingOutcome.OutsideBusinessHours, null);

        // Kilit + overlap kontrolü + insert tek pencerede: MySQL'de (constraint yok) TOCTOU'yu
        // GET_LOCK kapatır; Postgres/SQLite'ta kilit no-op, garanti exclusion constraint'te.
        IAsyncDisposable bookingLock;
        try
        {
            bookingLock = await _bookingLock.AcquireAsync(tenantId, ct);
        }
        catch (BookingLockTimeoutException)
        {
            return new BookingResult(BookingOutcome.Conflict, null); // güvenli taraf: rezervasyonu reddet
        }

        await using (bookingLock)
        {
            if (await HasOverlapAsync(tenantId, startUtc, endUtc, ct))
                return new BookingResult(BookingOutcome.Conflict, null);

            var appt = new Appointment
            {
                TenantId = tenantId,
                ServiceId = serviceId,
                StartUtc = startUtc,
                EndUtc = endUtc,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
            };
            _db.Appointments.Add(appt);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Postgres exclusion constraint eşzamanlı çakışmayı reddetti (TOCTOU). Kaydı geri al,
                // gerçekten örtüşme varsa Conflict; değilse beklenmeyen hata → rethrow.
                _db.Entry(appt).State = EntityState.Detached;
                if (await HasOverlapAsync(tenantId, startUtc, endUtc, ct))
                    return new BookingResult(BookingOutcome.Conflict, null);
                throw;
            }

            return new BookingResult(BookingOutcome.Booked, appt);
        }
    }

    private Task<bool> HasOverlapAsync(Guid tenantId, DateTime startUtc, DateTime endUtc, CancellationToken ct) =>
        _db.Appointments.AnyAsync(
            a => a.TenantId == tenantId
                 && a.Status == AppointmentStatus.Booked
                 && a.StartUtc < endUtc && a.EndUtc > startUtc, ct);
}
