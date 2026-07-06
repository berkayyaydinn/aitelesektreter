using Microsoft.EntityFrameworkCore;

namespace VoiceReception.Api.Data;

/// <summary>
/// MySQL migration seti için türetilmiş context. Model AppDbContext ile birebir aynı;
/// tek amacı EF'in MySQL migration'larını (Migrations/MySql) Postgres setinden ayrı
/// snapshot'la yönetmesi. Servisler AppDbContext enjekte etmeye devam eder
/// (DI: AddDbContext&lt;AppDbContext, MySqlAppDbContext&gt;).
/// </summary>
public sealed class MySqlAppDbContext : AppDbContext
{
    public MySqlAppDbContext(DbContextOptions<MySqlAppDbContext> options) : base(options) { }
}
