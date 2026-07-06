namespace VoiceReception.Api.Domain;

/// <summary>KVKK imha denetim kaydı — her RetentionSweeper taraması bir satır yazar.
/// "Ne zaman, neyi, kaç adet imha ettik" sorusuna kanıt (saklama-imha politikası §5).</summary>
public class RetentionRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime RanAt { get; set; }

    public int CallLogsDeleted { get; set; }
    public int TurnsDeleted { get; set; }
    public int RecordingsCleared { get; set; }
    public int MessageLogsDeleted { get; set; }
    public int AppointmentsAnonymized { get; set; }
    public int OrdersAnonymized { get; set; }
}
