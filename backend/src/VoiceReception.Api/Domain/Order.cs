namespace VoiceReception.Api.Domain;

/// <summary>Sesli ajanın aldığı sipariş (MVP: serbest metin kalemler).</summary>
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Items { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Received;
    /// <summary>Soft-delete: true ise tüm sorgulardan gizlenir.</summary>
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum OrderStatus
{
    Received = 0,
    Confirmed = 1,
    Cancelled = 2,
}
