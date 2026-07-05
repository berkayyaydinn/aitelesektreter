using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VoiceReception.Tests;

/// <summary>Gerçek HTTP pipeline'ı ayağa kaldırır (WebApplicationFactory).
///
/// Lokal modda izole bir SQLite dosyasıyla çalışır — Postgres/Docker gerektirmez. Her factory
/// örneği kendi dosyasını kullanır ve dispose'ta siler (test izolasyonu).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Key = "test-int-key";
    public const string NetsantralToken = "test-netsantral-token";
    public const string NetsantralAgentDid = "08509990000";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tele-it-{Guid.NewGuid():N}.db");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "sqlite",
                ["SQLITE_PATH"] = $"Data Source={_dbPath}",
                ["MESSAGING_PROVIDER"] = "console",
                ["SMS_PROVIDER"] = "console",
                ["REMINDERS_ENABLED"] = "false", // arka plan döngüsü testlerde deterministik olsun
                ["INTERNAL_API_KEY"] = Key,
                ["NETSANTRAL_WEBHOOK_TOKEN"] = NetsantralToken,
                ["NETSANTRAL_AGENT_DID"] = NetsantralAgentDid,
                ["RATE_LIMIT_PER_SECOND"] = "100000", // paralel testler rate limit'e takılmasın
            });
        });
        // Sabit saat: testler 2026-06-15 randevusu kullanıyor; "şimdi"yi öncesine sabitle ki
        // geçmiş-filtresi onları reddetmesin (wall-clock'tan bağımsız deterministik).
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new TestTimeProvider(new DateTime(2026, 6, 1, 0, 0, 0)));
        });
        return base.CreateHost(builder);
    }

    /// <summary>Her isteğe X-Internal-Key ekleyen client — auth gerektiren uçları test ederken kullanılır.
    /// (İstek kendi header'ını taşıyorsa default uygulanmaz; Keyed() helper'larıyla çakışmaz.)</summary>
    public HttpClient CreateKeyedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort temizlik */ }
    }
}
