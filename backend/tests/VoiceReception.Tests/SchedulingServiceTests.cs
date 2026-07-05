using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Scheduling;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>SchedulingService iş kuralları — SQLite in-memory (Postgres gerektirmez).
///
/// Saat sabitlenir (TestTimeProvider): testler 2026-06-15 (Pazartesi) kullanır; varsayılan "şimdi"
/// 2026-06-01 → randevular gelecekte kalır. Geçmiş/şimdi bağımlı senaryolar yerel saat enjekte eder.
/// </summary>
public class SchedulingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly SchedulingService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _serviceId = Guid.NewGuid();

    // 2026-06-15 = Pazartesi. Varsayılan saat (2026-06-01) bundan önce → randevular "gelecek".
    private static readonly DateOnly Monday = new(2026, 6, 15);
    private static readonly DateTime DefaultNow = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    public SchedulingServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = Sut(DefaultNow);

        Seed();
    }

    /// <summary>Belirtilen "şimdi" ile bir SchedulingService örneği.</summary>
    private SchedulingService Sut(DateTime nowUtc) => new(_db, new TestTimeProvider(nowUtc));

    private void Seed()
    {
        // Varsayılan: 60 dk hizmet, Pazartesi 09:00-12:00 açık.
        _db.Tenants.Add(new Tenant { Id = _tenantId, BusinessName = "Test İşletme" });
        _db.Services.Add(new Service { Id = _serviceId, TenantId = _tenantId, Name = "Danışma", DurationMinutes = 60 });
        _db.BusinessHours.Add(new BusinessHour
        {
            TenantId = _tenantId,
            Day = DayOfWeek.Monday,
            OpenTime = new TimeOnly(9, 0),
            CloseTime = new TimeOnly(12, 0),
        });
        _db.SaveChanges();
    }

    /// <summary>İzole senaryo (ayrı tenant/hizmet/çalışma saati) tohumlar.</summary>
    private (Guid TenantId, Guid ServiceId) SeedScenario(
        TimeOnly open, TimeOnly close, int duration = 60, bool active = true,
        bool closed = false, DayOfWeek day = DayOfWeek.Monday)
    {
        var tid = Guid.NewGuid();
        var sid = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = tid, BusinessName = "Senaryo" });
        _db.Services.Add(new Service { Id = sid, TenantId = tid, Name = "S", DurationMinutes = duration, IsActive = active });
        _db.BusinessHours.Add(new BusinessHour { TenantId = tid, Day = day, OpenTime = open, CloseTime = close, IsClosed = closed });
        _db.SaveChanges();
        return (tid, sid);
    }

    // ═══════════════════════════════════════ AVAILABILITY ═══════════════════════════════════════

    [Fact] // A0: temel — 09:00-12:00, 60 dk → 3 slot
    public async Task A0_GetAvailability_returns_slots_within_business_hours()
    {
        var slots = await _sut.GetAvailabilityAsync(_tenantId, _serviceId, Monday, default);
        Assert.Equal(new[] { "09:00", "10:00", "11:00" }, slots);
    }

    [Fact] // A0b: tanımsız gün kapalı
    public async Task A0_GetAvailability_returns_empty_when_closed_day()
    {
        var sunday = new DateOnly(2026, 6, 14); // Pazar tanımsız
        var slots = await _sut.GetAvailabilityAsync(_tenantId, _serviceId, sunday, default);
        Assert.Empty(slots);
    }

    [Fact] // A1+A3: komşu randevu komşu slotu bloklamaz (10:00 dolu → 09:00, 11:00 boş)
    public async Task A1_adjacent_booking_does_not_block_neighbor_slots()
    {
        await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(10, 0), "Ali", "+90555", default);
        var slots = await _sut.GetAvailabilityAsync(_tenantId, _serviceId, Monday, default);
        Assert.Equal(new[] { "09:00", "11:00" }, slots);
    }

    [Fact] // A2: kapanış kısmi slotu eler (09:00-11:30, 60 dk → 09:00, 10:00)
    public async Task A2_partial_slot_at_close_is_excluded()
    {
        var (tid, sid) = SeedScenario(new TimeOnly(9, 0), new TimeOnly(11, 30));
        var slots = await _sut.GetAvailabilityAsync(tid, sid, Monday, default);
        Assert.Equal(new[] { "09:00", "10:00" }, slots);
    }

    [Fact] // A3: iptal edilen randevu slotu serbest bırakır
    public async Task A3_cancelled_appointment_frees_slot()
    {
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(10, 0), "Ali", "+90555", default);
        r.Appointment!.Status = AppointmentStatus.Cancelled;
        await _db.SaveChangesAsync();

        var slots = await _sut.GetAvailabilityAsync(_tenantId, _serviceId, Monday, default);
        Assert.Equal(new[] { "09:00", "10:00", "11:00" }, slots);
    }

    [Fact] // A4: çoklu dolu slot → yalnız boşlar (09:00 & 11:00 dolu → 10:00)
    public async Task A4_multiple_booked_slots_leave_only_free_ones()
    {
        await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "A", "+1", default);
        await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(11, 0), "B", "+2", default);
        var slots = await _sut.GetAvailabilityAsync(_tenantId, _serviceId, Monday, default);
        Assert.Equal(new[] { "10:00" }, slots);
    }

    [Fact] // A5: 30 dk hizmet → 3 saatte 6 slot
    public async Task A5_thirty_minute_service_yields_six_slots()
    {
        var (tid, sid) = SeedScenario(new TimeOnly(9, 0), new TimeOnly(12, 0), duration: 30);
        var slots = await _sut.GetAvailabilityAsync(tid, sid, Monday, default);
        Assert.Equal(new[] { "09:00", "09:30", "10:00", "10:30", "11:00", "11:30" }, slots);
    }

    [Fact] // A6: yanlış tenant → boş (izolasyon)
    public async Task A6_wrong_tenant_returns_empty()
    {
        var slots = await _sut.GetAvailabilityAsync(Guid.NewGuid(), _serviceId, Monday, default);
        Assert.Empty(slots);
    }

    [Fact] // A7: pasif hizmet → boş
    public async Task A7_inactive_service_returns_empty()
    {
        var (tid, sid) = SeedScenario(new TimeOnly(9, 0), new TimeOnly(12, 0), active: false);
        var slots = await _sut.GetAvailabilityAsync(tid, sid, Monday, default);
        Assert.Empty(slots);
    }

    [Fact] // A8: bugün için geçmiş slotlar elenir (now=10:30 → yalnız 11:00)
    public async Task A8_past_slots_filtered_for_today()
    {
        var sut = Sut(new DateTime(2026, 6, 15, 10, 30, 0, DateTimeKind.Utc));
        var slots = await sut.GetAvailabilityAsync(_tenantId, _serviceId, Monday, default);
        Assert.Equal(new[] { "11:00" }, slots);
    }

    // ═══════════════════════════════════════ CREATE ═══════════════════════════════════════

    [Fact] // C1: boş slota randevu
    public async Task C1_create_succeeds_on_free_slot()
    {
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Ayşe", "+90555", default);
        Assert.Equal(BookingOutcome.Booked, r.Outcome);
        Assert.NotNull(r.Appointment);
        Assert.Equal(1, await _db.Appointments.CountAsync());
    }

    [Fact] // C2: örtüşen slot → Conflict
    public async Task C2_create_rejects_overlapping_slot()
    {
        await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Ali", "+1", default);
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Veli", "+2", default);
        Assert.Equal(BookingOutcome.Conflict, r.Outcome);
        Assert.Null(r.Appointment);
        Assert.Equal(1, await _db.Appointments.CountAsync());
    }

    [Fact] // C3: komşu slotlar ikisi de olur (09:00 ve 10:00)
    public async Task C3_adjacent_creates_both_succeed()
    {
        var r1 = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "A", "+1", default);
        var r2 = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(10, 0), "B", "+2", default);
        Assert.Equal(BookingOutcome.Booked, r1.Outcome);
        Assert.Equal(BookingOutcome.Booked, r2.Outcome);
        Assert.Equal(2, await _db.Appointments.CountAsync());
    }

    [Fact] // C4: kapanış sonrası (13:00) → OutsideBusinessHours
    public async Task C4_create_after_close_is_rejected()
    {
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(13, 0), "Ali", "+1", default);
        Assert.Equal(BookingOutcome.OutsideBusinessHours, r.Outcome);
        Assert.Equal(0, await _db.Appointments.CountAsync());
    }

    [Fact] // C5: kapalı gün (Pazar) → OutsideBusinessHours
    public async Task C5_create_on_closed_day_is_rejected()
    {
        var sunday = new DateOnly(2026, 6, 14);
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, sunday, new TimeOnly(10, 0), "Ali", "+1", default);
        Assert.Equal(BookingOutcome.OutsideBusinessHours, r.Outcome);
    }

    [Fact] // C6: kapanışı aşan (11:30 + 60 dk, kapanış 12:00 → bitiş 12:30) → OutsideBusinessHours
    public async Task C6_create_overrunning_close_is_rejected()
    {
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(11, 30), "Ali", "+1", default);
        Assert.Equal(BookingOutcome.OutsideBusinessHours, r.Outcome);
    }

    [Fact] // C7: geçmiş saat → InPast (now=10:00, randevu 09:00)
    public async Task C7_create_in_past_is_rejected()
    {
        var sut = Sut(new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc));
        var r = await sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Ali", "+1", default);
        Assert.Equal(BookingOutcome.InPast, r.Outcome);
        Assert.Equal(0, await _db.Appointments.CountAsync());
    }

    [Fact] // C8: pasif hizmet → ServiceUnavailable
    public async Task C8_create_on_inactive_service_is_rejected()
    {
        var (tid, sid) = SeedScenario(new TimeOnly(9, 0), new TimeOnly(12, 0), active: false);
        var r = await _sut.CreateAppointmentAsync(tid, sid, Monday, new TimeOnly(9, 0), "Ali", "+1", default);
        Assert.Equal(BookingOutcome.ServiceUnavailable, r.Outcome);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
