namespace VoiceReception.Api.Compliance;

/// <summary>Türkiye yerel saati. TR kalıcı UTC+3 (DST yok).
/// İzinli arama saati kontrolü yerel saatle yapılmalı.</summary>
public static class TurkeyTime
{
    private const int OffsetHours = 3;

    public static DateTime Now() => DateTime.UtcNow.AddHours(OffsetHours);
}
