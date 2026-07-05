using System.Threading.RateLimiting;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VoiceReception.Api.Compliance;
using VoiceReception.Api.Crm;
using VoiceReception.Api.Data;
using VoiceReception.Api.Endpoints;
using VoiceReception.Api.Invoicing;
using VoiceReception.Api.Messaging;
using VoiceReception.Api.Messaging.Sms;
using VoiceReception.Api.Outbound;
using VoiceReception.Api.Reminders;
using VoiceReception.Api.Scheduling;

Env.TraversePath().Load(); // .env'i ortam değişkeni olarak yükler (varsa)

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// --- Veritabanı sağlayıcı anahtarı (swappable) ---
// DB_PROVIDER=sqlite  -> lokal test, Postgres/Docker gerektirmez (varsayılan)
// DB_PROVIDER=postgres -> üretim
var dbProvider = (builder.Configuration["DB_PROVIDER"] ?? "sqlite").ToLowerInvariant();

builder.Services.AddDbContext<AppDbContext>(o =>
{
    // Soft-delete query filter + required PhoneNumber→Tenant ilişkisi uyarısını bastır:
    // silinen tenant'ta by-did'in null dönmesi BEKLENEN davranış (DID routing durur).
    o.ConfigureWarnings(w => w.Ignore(
        CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    if (dbProvider == "postgres")
    {
        var cs = builder.Configuration["DATABASE_URL"]
            ?? throw new InvalidOperationException("DB_PROVIDER=postgres için DATABASE_URL gerekli");
        o.UseNpgsql(cs);
    }
    else
    {
        var cs = builder.Configuration["SQLITE_PATH"] ?? "Data Source=app.db";
        o.UseSqlite(cs);
    }
});

builder.Services.AddSingleton(TimeProvider.System); // SchedulingService saat kaynağı (geçmiş slot/randevu filtresi)
builder.Services.AddScoped<SchedulingService>();

// --- Rate limiting (kötüye kullanım kapısı) ---
// X-Internal-Key (yoksa IP) başına sabit pencere. Varsayılan 50/sn; testler RATE_LIMIT_PER_SECOND
// ile yükseltir (paralel test 429 yememeli). Gerçek çağıran bu sınıra yaklaşmaz.
var rateLimitPerSecond = int.TryParse(builder.Configuration["RATE_LIMIT_PER_SECOND"], out var rl) ? rl : 50;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.Request.Headers["X-Internal-Key"].ToString();
        if (string.IsNullOrEmpty(key))
            key = ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitPerSecond,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        });
    });
});

// --- Mesajlaşma sağlayıcı anahtarı (swappable) ---
// MESSAGING_PROVIDER=console -> dry-run (Meta token gerektirmez, varsayılan)
// MESSAGING_PROVIDER=whatsapp_cloud -> gerçek WhatsApp Cloud API
var messagingProvider = (builder.Configuration["MESSAGING_PROVIDER"] ?? "console").ToLowerInvariant();
if (messagingProvider == "whatsapp_cloud")
    builder.Services.AddHttpClient<IMessagingProvider, WhatsAppCloudProvider>();
else
    builder.Services.AddScoped<IMessagingProvider, ConsoleMessagingProvider>();

// --- SMS sağlayıcı anahtarı (swappable): hatırlatma SMS'leri ---
// SMS_PROVIDER=console -> dry-run (Netgsm gerektirmez, varsayılan) | netgsm -> gerçek Netgsm HTTP API
var smsProvider = (builder.Configuration["SMS_PROVIDER"] ?? "console").ToLowerInvariant();
if (smsProvider == "netgsm")
    builder.Services.AddHttpClient<ISmsProvider, NetgsmSmsProvider>();
else
    builder.Services.AddScoped<ISmsProvider, ConsoleSmsProvider>();

// --- Hatırlatma dağıtıcısı (arka plan servisi) ---
// Yaklaşan randevu + geciken ödeme SMS'lerini periyodik gönderir.
// REMINDERS_ENABLED=false -> kapalı (entegrasyon testleri deterministik kalsın).
if ((builder.Configuration["REMINDERS_ENABLED"] ?? "true").ToLowerInvariant() != "false")
    builder.Services.AddHostedService<ReminderDispatcher>();

// --- Giden kampanya + İYS (swappable) ---
// İYS onay istemcisi: lokal (Consents tablosu) | (üretim: gerçek İYS API)
builder.Services.AddScoped<IIysClient, LocalIysClient>();
builder.Services.AddScoped<IysComplianceService>();
// Arama yerleştirme: OUTBOUND_DIALER=console (dry-run, varsayılan) | livekit (gerçek SIP outbound)
var outboundDialer = (builder.Configuration["OUTBOUND_DIALER"] ?? "console").ToLowerInvariant();
if (outboundDialer == "livekit")
    builder.Services.AddHttpClient<IOutboundDialer, LiveKitOutboundDialer>();
else
    builder.Services.AddScoped<IOutboundDialer, ConsoleOutboundDialer>();
builder.Services.AddScoped<CampaignRunner>();

// --- Fatura kesme (swappable) ---
// INVOICE_PROVIDER=console (dry-run) | (üretim: gib/parasut)
builder.Services.AddScoped<IInvoiceProvider, ConsoleInvoiceProvider>();

// --- CRM aynalama (swappable) ---
// CRM_PROVIDER=none -> kapalı (varsayılan) | mirbal -> Mirbal CRM /api/crm (X-Api-Key)
// Randevu/sipariş/çağrı olayları best-effort olarak CRM'e aynalanır (akışı bloklamaz).
var crmOptions = new CrmOptions
{
    Provider = (builder.Configuration["CRM_PROVIDER"] ?? "none").ToLowerInvariant(),
    BaseUrl = builder.Configuration["CRM_BASE_URL"],
    ApiKey = builder.Configuration["CRM_API_KEY"],
};
builder.Services.AddSingleton(crmOptions);
if (crmOptions.Provider == "mirbal")
{
    var baseUrl = crmOptions.BaseUrl
        ?? throw new InvalidOperationException("CRM_PROVIDER=mirbal için CRM_BASE_URL gerekli");
    builder.Services.AddHttpClient<ICrmSink, MirbalCrmSink>(c =>
    {
        c.BaseAddress = new Uri(baseUrl);
        c.Timeout = TimeSpan.FromSeconds(10);
        if (!string.IsNullOrEmpty(crmOptions.ApiKey))
            c.DefaultRequestHeaders.Add("X-Api-Key", crmOptions.ApiKey);
    });
}
else
{
    builder.Services.AddSingleton<ICrmSink, NullCrmSink>();
}

var app = builder.Build();

app.UseRateLimiter();

// Şema hazırla: SQLite lokal -> EnsureCreated (migration gerekmez); Postgres -> Migrate.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (dbProvider == "postgres") db.Database.Migrate();
    else db.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", db = dbProvider, messaging = messagingProvider, sms = smsProvider, crm = crmOptions.Provider }));

app.MapNetsantralApi(); // Netsantral Custom API webhook — çağrı karar/yönlendirme (token auth)
app.MapInternalApi();   // voice worker tüketir (X-Internal-Key)
app.MapTenantApi();     // dashboard / onboarding
app.MapCampaignApi();   // giden kampanya + İYS
app.MapCallReadApi();   // çağrı geçmişi + transkript okuma (CRM/admin, X-Internal-Key)

app.Run();

public partial class Program { } // WebApplicationFactory integration testleri için
