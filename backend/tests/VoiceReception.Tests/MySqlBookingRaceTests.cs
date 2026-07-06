using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Scheduling;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>
/// Gerçek MySQL'e karşı eşzamanlı rezervasyon yarışı — GET_LOCK korumasının uçtan uca kanıtı.
/// MYSQL_TEST_URL yoksa sessizce atlanır (CI'da opsiyonel):
///   MYSQL_TEST_URL="Server=127.0.0.1;Port=3306;Database=telesekreter_test;User=root;Password=..."
/// </summary>
public class MySqlBookingRaceTests
{
    private static readonly DateOnly Monday = new(2026, 6, 15);
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact] // R1: aynı slota iki eşzamanlı istek → tam bir Booked + bir Conflict
    public async Task R1_concurrent_bookings_yield_exactly_one_success()
    {
        var url = Environment.GetEnvironmentVariable("MYSQL_TEST_URL");
        if (string.IsNullOrWhiteSpace(url)) return; // MySQL yok → atla

        var options = new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySql(url, ServerVersion.AutoDetect(url))
            .Options;

        var tenantId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        await using (var seed = new MySqlAppDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Tenants.Add(new Tenant { Id = tenantId, BusinessName = "Yarış Testi" });
            seed.Services.Add(new Service { Id = serviceId, TenantId = tenantId, Name = "S", DurationMinutes = 60 });
            seed.BusinessHours.Add(new BusinessHour
            {
                TenantId = tenantId,
                Day = DayOfWeek.Monday,
                OpenTime = new TimeOnly(9, 0),
                CloseTime = new TimeOnly(12, 0),
            });
            await seed.SaveChangesAsync();
        }

        // İki bağımsız context/session — üretimdeki iki paralel HTTP isteğini taklit eder.
        async Task<BookingResult> BookAsync(string name)
        {
            await using var db = new MySqlAppDbContext(options);
            var sut = new SchedulingService(db, new TestTimeProvider(Now), new MySqlBookingLock(db));
            return await sut.CreateAppointmentAsync(tenantId, serviceId, Monday, new TimeOnly(9, 0), name, "+90555", default);
        }

        var results = await Task.WhenAll(BookAsync("A"), BookAsync("B"));

        Assert.Equal(1, results.Count(r => r.Outcome == BookingOutcome.Booked));
        Assert.Equal(1, results.Count(r => r.Outcome == BookingOutcome.Conflict));

        await using var verify = new MySqlAppDbContext(options);
        var count = await verify.Appointments.CountAsync(a => a.TenantId == tenantId);
        Assert.Equal(1, count);
    }
}
