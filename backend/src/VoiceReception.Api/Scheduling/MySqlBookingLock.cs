using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;

namespace VoiceReception.Api.Scheduling;

/// <summary>
/// MySQL GET_LOCK/RELEASE_LOCK ile tenant bazlı rezervasyon kilidi. GET_LOCK session-scoped
/// olduğundan bağlantı açık tutulur; overlap kontrolü ve insert aynı DbContext (dolayısıyla
/// aynı bağlantı/session) üzerinden aktığı sürece TOCTOU penceresi kapalıdır.
/// </summary>
public sealed class MySqlBookingLock : IBookingLock
{
    /// <summary>Kilit bekleme süresi (sn). Dolarsa BookingLockTimeoutException → çağıran Conflict döner.</summary>
    internal const int TimeoutSeconds = 5;

    private readonly AppDbContext _db;

    public MySqlBookingLock(AppDbContext db) => _db = db;

    /// <summary>GET_LOCK adı (MySQL sınırı 64 karakter; "appt:" + 32 hex = 37).</summary>
    internal static string LockName(Guid tenantId) => $"appt:{tenantId:N}";

    public async Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct)
    {
        // Bağlantıyı sabitle: kilit bu session'a bağlı, EF'in aç/kapat davranışına bırakılamaz.
        var wasClosed = _db.Database.GetDbConnection().State != ConnectionState.Open;
        if (wasClosed) await _db.Database.OpenConnectionAsync(ct);

        try
        {
            var acquired = await ExecuteLockFunctionAsync("GET_LOCK", LockName(tenantId), TimeoutSeconds, ct);
            if (acquired != 1)
                throw new BookingLockTimeoutException(tenantId);
        }
        catch
        {
            if (wasClosed) await _db.Database.CloseConnectionAsync();
            throw;
        }

        return new Releaser(this, tenantId, wasClosed);
    }

    private async Task<long> ExecuteLockFunctionAsync(string function, string name, int? timeoutSeconds, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = timeoutSeconds is null
            ? $"SELECT {function}(@name);"
            : $"SELECT {function}(@name, @timeout);";
        AddParameter(cmd, "@name", name);
        if (timeoutSeconds is not null) AddParameter(cmd, "@timeout", timeoutSeconds.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private sealed class Releaser(MySqlBookingLock owner, Guid tenantId, bool closeConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await owner.ExecuteLockFunctionAsync("RELEASE_LOCK", LockName(tenantId), null, CancellationToken.None);
            }
            finally
            {
                // Bağlantı düşse bile MySQL session kilidini kendisi bırakır; burada sızıntı yok.
                if (closeConnection) await owner._db.Database.CloseConnectionAsync();
            }
        }
    }
}
