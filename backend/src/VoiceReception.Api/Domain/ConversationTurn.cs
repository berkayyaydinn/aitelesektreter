namespace VoiceReception.Api.Domain;

/// <summary>Bir çağrının tek diyalog turu (kullanıcı veya asistan). CallLog'a bağlı.</summary>
public class ConversationTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CallLogId { get; set; }

    /// <summary>"user" | "assistant" — konuşan taraf.</summary>
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
