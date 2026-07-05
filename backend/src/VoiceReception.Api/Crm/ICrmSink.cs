namespace VoiceReception.Api.Crm;

/// <summary>
/// CRM aynalama soyutlaması (swappable: <c>none</c> = kapalı, <c>mirbal</c> = Mirbal CRM /api/crm).
///
/// Tüm çağrılar <b>best-effort</b>: CRM erişilemese/hata dönse bile telesekreter akışını (randevu,
/// sipariş, çağrı) bloklamaz. Uygulamalar kendi hatalarını yutar; çağıran tarafın try/catch'ine
/// ihtiyaç yoktur.
/// </summary>
public interface ICrmSink
{
    /// <summary>CRM aynalama açık mı? Kapalıysa çağıran taraf hiç uğraşmaz.</summary>
    bool Enabled { get; }

    /// <summary>Telefondan mevcut CRM müşterisini bulur; yoksa veya hata olursa null.</summary>
    Task<int?> FindCustomerIdByPhoneAsync(string? phone, CancellationToken ct = default);

    /// <summary>Randevuyu CRM takvimine aynalar.</summary>
    Task MirrorAppointmentAsync(CrmAppointment appointment, CancellationToken ct = default);

    /// <summary>Sipariş/yeni arayanı CRM'e lead olarak aynalar (Kaynak=Telesekreter).</summary>
    Task MirrorLeadAsync(CrmLead lead, CancellationToken ct = default);

    /// <summary>Çağrı olayını CRM zaman tüneline aktivite olarak aynalar.</summary>
    Task MirrorActivityAsync(CrmActivity activity, CancellationToken ct = default);
}
