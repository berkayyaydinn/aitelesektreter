using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>
/// DB_PROVIDER=mysql konfigürasyon sözleşmesi. Gerçek MySQL gerektirmez:
/// eksik DATABASE_URL startup'ta hızlı ve açık hata vermeli (sessiz sqlite fallback OLMAZ).
/// </summary>
public class MySqlProviderTests
{
    [Fact] // CFG1: DATABASE_URL yoksa startup açık hatayla düşer
    public void CFG1_mysql_without_database_url_fails_fast()
    {
        using var factory = new MySqlMisconfiguredFactory();
        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("DATABASE_URL", Flatten(ex!));
    }

    /// <summary>İç içe sarılmış exception mesajlarını düzleştirir (DI/host sarmalayabilir).</summary>
    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        if (ex is AggregateException agg)
            messages.AddRange(agg.InnerExceptions.Select(Flatten));
        return string.Join(" | ", messages);
    }

    private sealed class MySqlMisconfiguredFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DB_PROVIDER"] = "mysql",
                    // DATABASE_URL bilerek yok — açık hata bekleniyor.
                    ["MYSQL_SERVER_VERSION"] = "8.0.36", // AutoDetect bağlantı denemesin
                    ["MESSAGING_PROVIDER"] = "console",
                    ["SMS_PROVIDER"] = "console",
                    ["REMINDERS_ENABLED"] = "false",
                    ["RETENTION_ENABLED"] = "false",
                    ["INTERNAL_API_KEY"] = "cfg-test-key",
                });
            });
            return base.CreateHost(builder);
        }
    }
}
