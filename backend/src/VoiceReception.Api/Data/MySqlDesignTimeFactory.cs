using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VoiceReception.Api.Data;

/// <summary>
/// `dotnet ef migrations add X --context MySqlAppDbContext` için design-time factory.
/// Sabit ServerVersion + dummy bağlantı: migration üretimi canlı MySQL istemez
/// (migrations remove'un Postgres'te 5432'ye bağlanmaya çalışması dersinden).
/// </summary>
public sealed class MySqlDesignTimeFactory : IDesignTimeDbContextFactory<MySqlAppDbContext>
{
    public MySqlAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySql(
                "Server=localhost;Database=design_time_only",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        return new MySqlAppDbContext(options);
    }
}
