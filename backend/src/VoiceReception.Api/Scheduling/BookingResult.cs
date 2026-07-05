using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Scheduling;

/// <summary>Randevu oluşturma sonucu — "conflict" dışı ret sebeplerini de ayırt eder.</summary>
public enum BookingOutcome
{
    /// <summary>Randevu oluşturuldu.</summary>
    Booked,

    /// <summary>İstenen aralık dolu (app-seviye veya DB exclusion constraint).</summary>
    Conflict,

    /// <summary>Saat çalışma penceresi dışında veya gün kapalı.</summary>
    OutsideBusinessHours,

    /// <summary>Hizmet yok veya pasif (IsActive=false).</summary>
    ServiceUnavailable,

    /// <summary>İstenen başlangıç şu andan önce.</summary>
    InPast,
}

/// <summary>Oluşturma sonucu + (başarılıysa) randevu kaydı.</summary>
public readonly record struct BookingResult(BookingOutcome Outcome, Appointment? Appointment);
