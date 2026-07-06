namespace VoiceReception.Api.Scheduling;

/// <summary>
/// Randevu oluşturmayı tenant bazında serileştiren kilit. Postgres'te exclusion constraint
/// (appt_no_overlap) atomik garantiyi DB'de verdiğinden kilit no-op; MySQL'de eşdeğer
/// constraint olmadığından GET_LOCK ile check+insert penceresi kapatılır.
/// </summary>
public interface IBookingLock
{
    /// <summary>Tenant kilidini alır; dönen handle dispose edilince bırakılır.</summary>
    /// <exception cref="BookingLockTimeoutException">Kilit süresinde alınamazsa.</exception>
    Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Kilit zaman aşımı — çağıran güvenli tarafta kalıp Conflict döndürmeli.</summary>
public sealed class BookingLockTimeoutException(Guid tenantId)
    : Exception($"Tenant {tenantId} için rezervasyon kilidi alınamadı (zaman aşımı).");

/// <summary>Postgres (constraint korur) ve SQLite (tek kullanıcı lokal) için no-op kilit.</summary>
public sealed class NoopBookingLock : IBookingLock
{
    private sealed class NoopHandle : IAsyncDisposable
    {
        public static readonly NoopHandle Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable>(NoopHandle.Instance);
}
