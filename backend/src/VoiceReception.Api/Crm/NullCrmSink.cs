namespace VoiceReception.Api.Crm;

/// <summary>CRM aynalama kapalıyken kullanılan no-op sink (CRM_PROVIDER=none/varsayılan).</summary>
public sealed class NullCrmSink : ICrmSink
{
    public bool Enabled => false;

    public Task<int?> FindCustomerIdByPhoneAsync(string? phone, CancellationToken ct = default)
        => Task.FromResult<int?>(null);

    public Task MirrorAppointmentAsync(CrmAppointment appointment, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task MirrorLeadAsync(CrmLead lead, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task MirrorActivityAsync(CrmActivity activity, CancellationToken ct = default)
        => Task.CompletedTask;
}
