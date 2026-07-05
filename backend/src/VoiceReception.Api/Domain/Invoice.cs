namespace VoiceReception.Api.Domain;

/// <summary>İşletme sahibinin telefonla (sesli komutla) kestiği fatura.</summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string? Description { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Created;

    /// <summary>Fatura sağlayıcısının (GİB e-Arşiv / Paraşüt vb.) döndürdüğü kimlik.</summary>
    public string? ProviderInvoiceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ödeme vadesi (UTC). null = hatırlatma planlanmadı.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Ödeme durumu — kesim (Status) yaşam döngüsünden bağımsız dik kavram.</summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    /// <summary>Geciken ödeme hatırlatması gönderildiği an (UTC). null = henüz hatırlatılmadı.</summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>Soft-delete: true ise tüm sorgulardan gizlenir.</summary>
    public bool IsDeleted { get; set; }
}

public enum InvoiceStatus
{
    Created = 0,   // backend'de oluşturuldu
    Issued = 1,    // sağlayıcıda kesildi
    Failed = 2,
}

public enum PaymentStatus
{
    Unpaid = 0,
    Paid = 1,
}
