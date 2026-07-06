using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Scheduling;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>
/// IBookingLock sözleşmesi — SchedulingService kilidi doğru anlarda alıp bırakmalı.
/// MySQL'de exclusion constraint olmadığından TOCTOU koruması bu kilide dayanır;
/// erken dönüşlerde (hizmet yok, geçmiş, kapalı) kilit hiç alınmamalı (gereksiz serileştirme).
/// </summary>
public class BookingLockTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly FakeBookingLock _lock = new();
    private readonly SchedulingService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _serviceId = Guid.NewGuid();

    private static readonly DateOnly Monday = new(2026, 6, 15);
    private static readonly DateTime DefaultNow = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    public BookingLockTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new SchedulingService(_db, new TestTimeProvider(DefaultNow), _lock);

        _db.Tenants.Add(new Tenant { Id = _tenantId, BusinessName = "Kilit Test" });
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

    [Fact] // L1: başarılı booking kilidi bir kez alır ve bırakır
    public async Task L1_successful_booking_acquires_and_releases_lock_once()
    {
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Ali", "+90555", default);

        Assert.Equal(BookingOutcome.Booked, r.Outcome);
        Assert.Equal(1, _lock.AcquireCount);
        Assert.Equal(1, _lock.ReleaseCount);
    }

    [Fact] // L2: pasif hizmet erken döner — kilit hiç alınmaz
    public async Task L2_early_return_does_not_touch_lock()
    {
        var r = await _sut.CreateAppointmentAsync(_tenantId, Guid.NewGuid(), Monday, new TimeOnly(9, 0), "Ali", "+90555", default);

        Assert.Equal(BookingOutcome.ServiceUnavailable, r.Outcome);
        Assert.Equal(0, _lock.AcquireCount);
    }

    [Fact] // L3: çakışma (Conflict) yolunda da kilit bırakılır (sızıntı yok)
    public async Task L3_conflict_path_still_releases_lock()
    {
        await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "A", "+1", default);
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "B", "+2", default);

        Assert.Equal(BookingOutcome.Conflict, r.Outcome);
        Assert.Equal(2, _lock.AcquireCount);
        Assert.Equal(2, _lock.ReleaseCount);
    }

    [Fact] // L4: kilit alınamazsa (timeout) güvenli taraf: Conflict, kayıt yok
    public async Task L4_lock_timeout_returns_conflict_without_saving()
    {
        _lock.FailNextAcquire = true;
        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Ali", "+90555", default);

        Assert.Equal(BookingOutcome.Conflict, r.Outcome);
        Assert.Equal(0, await _db.Appointments.CountAsync());
    }

    [Fact] // L5: overlap kontrolü kilit İÇİNDE yapılmalı (TOCTOU penceresi kapanır)
    public async Task L5_overlap_check_happens_while_lock_held()
    {
        // Kilit alındığı anda rakip bir randevu araya girer; check kilit içindeyse bunu görür.
        _lock.OnAcquired = () =>
        {
            var rivalDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
            rivalDb.Appointments.Add(new Appointment
            {
                TenantId = _tenantId,
                ServiceId = _serviceId,
                StartUtc = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc),
                CustomerName = "Rakip",
                CustomerPhone = "+9",
            });
            rivalDb.SaveChanges();
            rivalDb.Dispose();
        };

        var r = await _sut.CreateAppointmentAsync(_tenantId, _serviceId, Monday, new TimeOnly(9, 0), "Ali", "+90555", default);

        Assert.Equal(BookingOutcome.Conflict, r.Outcome);
        Assert.Equal(1, await _db.Appointments.CountAsync()); // yalnız rakip kayıt
    }

    [Fact] // M1: MySQL kilit adı deterministik ve GET_LOCK'ın 64 karakter sınırı içinde
    public void M1_mysql_lock_name_is_stable_and_within_limit()
    {
        var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var name = MySqlBookingLock.LockName(tenantId);

        Assert.Equal("appt:aaaaaaaabbbbccccddddeeeeeeeeeeee", name);
        Assert.True(name.Length <= 64);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}

/// <summary>Kilit davranışını gözlemleyen sahte — sayaçlar + isteğe bağlı acquire-anı callback'i.</summary>
internal sealed class FakeBookingLock : IBookingLock
{
    public int AcquireCount { get; private set; }
    public int ReleaseCount { get; private set; }
    public bool FailNextAcquire { get; set; }
    public Action? OnAcquired { get; set; }

    public Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct)
    {
        if (FailNextAcquire)
        {
            FailNextAcquire = false;
            throw new BookingLockTimeoutException(tenantId);
        }
        AcquireCount++;
        OnAcquired?.Invoke();
        return Task.FromResult<IAsyncDisposable>(new Releaser(this));
    }

    private sealed class Releaser(FakeBookingLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }
}
