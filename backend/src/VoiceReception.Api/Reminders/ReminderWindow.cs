namespace VoiceReception.Api.Reminders;

/// <summary>Hatırlatma gönderim penceresi (sessiz saat) kontrolü — saf, test edilebilir.
///
/// İzinli pencere dışı = sessiz saat → SMS gönderilmez (işaretlenmez, sonraki tick tekrar dener).
/// Saat yerel TR (UTC+3) ile değerlendirilmeli.
/// </summary>
public static class ReminderWindow
{
    /// <summary>Verilen yerel saat gönderim penceresi içinde mi? [start, end) yarı-açık aralık.</summary>
    public static bool IsWithinSendingWindow(TimeOnly nowLocal, TimeOnly start, TimeOnly end)
    {
        if (start == end) return true;            // pencere yok → her zaman izinli
        if (start < end) return nowLocal >= start && nowLocal < end;
        // Gece yarısını saran pencere (ör. 21:00-09:00) — gelecekteki esneklik için.
        return nowLocal >= start || nowLocal < end;
    }
}
