using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<CallLog> CallLogs => Set<CallLog>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignTarget> CampaignTargets => Set<CampaignTarget>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<RetentionRun> RetentionRuns => Set<RetentionRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // DID routing'in kalbi — benzersiz olmalı, hızlı lookup.
        b.Entity<PhoneNumber>().HasIndex(p => p.Did).IsUnique();

        // Çakışma sorgusu için randevu zaman indeksleri.
        b.Entity<Appointment>().HasIndex(a => new { a.TenantId, a.StartUtc });
        // Hatırlatma dağıtıcısının yaklaşan randevu taraması (durum + başlangıç).
        b.Entity<Appointment>().HasIndex(a => new { a.Status, a.StartUtc });

        b.Entity<Service>().HasIndex(s => s.TenantId);
        b.Entity<BusinessHour>().HasIndex(h => new { h.TenantId, h.Day });
        b.Entity<MessageLog>().HasIndex(m => m.TenantId);

        // İYS onay lookup'ı (numara + tip) — kampanya kapısı sık sorgular.
        b.Entity<Consent>().HasIndex(c => new { c.TenantId, c.CustomerPhone, c.Type });
        // Kampanya hedef tarama (kampanya + durum).
        b.Entity<CampaignTarget>().HasIndex(t => new { t.CampaignId, t.Status });
        b.Entity<Invoice>().HasIndex(i => i.TenantId);
        b.Entity<Invoice>().Property(i => i.Amount).HasPrecision(18, 2);
        // Geciken ödeme taraması (ödeme durumu + vade).
        b.Entity<Invoice>().HasIndex(i => new { i.PaymentStatus, i.DueDate });

        // Çağrı transkripti — tura göre sıralı okuma (çağrı + zaman).
        b.Entity<ConversationTurn>().HasIndex(t => new { t.CallLogId, t.OccurredAt });

        // Retention taramaları (yaş kesme noktası sorguları).
        b.Entity<CallLog>().HasIndex(c => c.StartedAt);
        b.Entity<MessageLog>().HasIndex(m => m.CreatedAt);

        // Soft-delete: silinmiş kayıtlar tüm sorgulardan otomatik gizlenir (geçmiş korunur).
        b.Entity<Tenant>().HasQueryFilter(t => !t.IsDeleted);
        b.Entity<Service>().HasQueryFilter(s => !s.IsDeleted);
        b.Entity<Appointment>().HasQueryFilter(a => !a.IsDeleted);
        b.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);
        b.Entity<Invoice>().HasQueryFilter(i => !i.IsDeleted);

        base.OnModelCreating(b);
    }
}
