using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Crm;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Invoicing;
using VoiceReception.Api.Messaging;
using VoiceReception.Api.Scheduling;

namespace VoiceReception.Api.Endpoints;

/// <summary>Voice worker'ın tükettiği internal API. X-Internal-Key ile korunur.</summary>
public static class InternalApiEndpoints
{
    public static void MapInternalApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").AddEndpointFilter(InternalKey.Filter);

        group.MapGet("/tenants/by-did/{did}", GetTenantByDid);
        group.MapPost("/availability", GetAvailability);
        group.MapPost("/appointments", CreateAppointment);
        group.MapPost("/orders", CreateOrder);
        group.MapPost("/invoices", CreateInvoice);
        group.MapPost("/calls/events", RecordCallEvent);
    }

    private static async Task<IResult> GetTenantByDid(string did, AppDbContext db, CancellationToken ct)
    {
        var phone = await TenantLookup.FindByDidAsync(db, did, ct);

        if (phone?.Tenant is null) return Results.NotFound();
        var t = phone.Tenant;

        var hoursText = string.Join(", ", t.BusinessHours
            .Where(h => !h.IsClosed)
            .OrderBy(h => h.Day)
            .Select(h => $"{h.Day} {h.OpenTime:HH\\:mm}-{h.CloseTime:HH\\:mm}"));

        var services = t.Services.Where(s => s.IsActive)
            .Select(s => new TenantServiceDto(s.Id.ToString(), s.Name, s.DurationMinutes))
            .ToList();

        return Results.Ok(new TenantConfigResponse(
            t.Id.ToString(), t.BusinessName, t.ExtraPrompt, hoursText, services, t.OwnerPhone,
            t.PromptTemplate));
    }

    /// <summary>Fatura keser. SADECE işletme sahibi: callerPhone == tenant.OwnerPhone, yoksa 403.</summary>
    private static async Task<IResult> CreateInvoice(
        CreateInvoiceRequest req, AppDbContext db, IInvoiceProvider provider, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TenantId, out var tenantId))
            return Results.BadRequest("Geçersiz tenant");
        if (req.Amount <= 0)
            return Results.BadRequest("Tutar pozitif olmalı");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        // Sahip doğrulama — fatura kesme yetkisi sadece kayıtlı sahip numarasında.
        if (string.IsNullOrEmpty(tenant.OwnerPhone) || tenant.OwnerPhone != req.CallerPhone)
            return Results.Json(new { error = "Fatura kesme yetkisi yok (sahip değil)" }, statusCode: 403);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerName = req.CustomerName,
            CustomerPhone = req.CustomerPhone,
            Amount = req.Amount,
            Description = req.Description,
        };

        var result = await provider.IssueAsync(invoice, ct);
        invoice.Status = result.Success ? InvoiceStatus.Issued : InvoiceStatus.Failed;
        invoice.ProviderInvoiceId = result.ProviderInvoiceId;

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { invoiceId = invoice.Id, status = invoice.Status.ToString(), invoice.ProviderInvoiceId });
    }

    private static async Task<IResult> GetAvailability(
        AvailabilityRequest req, SchedulingService scheduling, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TenantId, out var tenantId) ||
            !Guid.TryParse(req.ServiceId, out var serviceId) ||
            !DateOnly.TryParse(req.Date, out var date))
            return Results.BadRequest("Geçersiz parametre");

        var slots = await scheduling.GetAvailabilityAsync(tenantId, serviceId, date, ct);
        return Results.Ok(new AvailabilityResponse(slots));
    }

    private static async Task<IResult> CreateAppointment(
        CreateAppointmentRequest req, SchedulingService scheduling, AppDbContext db,
        IMessagingProvider messaging, IConfiguration config, ICrmSink crm, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TenantId, out var tenantId) ||
            !Guid.TryParse(req.ServiceId, out var serviceId) ||
            !DateOnly.TryParse(req.Date, out var date) ||
            !TimeOnly.TryParse(req.Time, out var time))
            return Results.BadRequest("Geçersiz parametre");

        var result = await scheduling.CreateAppointmentAsync(
            tenantId, serviceId, date, time, req.CustomerName, req.CustomerPhone, ct);

        // Ret sebepleri voice agent'a ayrı status olarak taşınır (tools.py her birine özel yanıt verir).
        if (result.Outcome != BookingOutcome.Booked)
        {
            var status = result.Outcome switch
            {
                BookingOutcome.Conflict => "conflict",
                BookingOutcome.OutsideBusinessHours => "outside_hours",
                BookingOutcome.ServiceUnavailable => "service_unavailable",
                BookingOutcome.InPast => "past",
                _ => "conflict",
            };
            return Results.Ok(new { status });
        }

        var appt = result.Appointment!;

        // Görüşme sonrası bilgilendirme: randevu onayı template'i (best-effort, çağrıyı bloklamaz).
        await SendAppointmentNotificationAsync(tenantId, req, db, messaging, config, ct);

        // CRM aynalama: müşteriyi telefondan eşleştir, randevuyu CRM takvimine aktar (best-effort).
        await MirrorAppointmentToCrmAsync(crm, appt, req, ct);

        return Results.Ok(new { status = "booked", appointmentId = appt.Id });
    }

    /// <summary>Randevuyu CRM'e aynalar. Sink hatalarını kendi içinde yutar — çağrıyı bloklamaz.</summary>
    private static async Task MirrorAppointmentToCrmAsync(
        ICrmSink crm, Appointment appt, CreateAppointmentRequest req, CancellationToken ct)
    {
        if (!crm.Enabled) return;

        var customerId = await crm.FindCustomerIdByPhoneAsync(req.CustomerPhone, ct);
        await crm.MirrorAppointmentAsync(new CrmAppointment(
            Title: $"Telesekreter randevu — {req.CustomerName}",
            Description: $"Telefon: {req.CustomerPhone}",
            StartUtc: appt.StartUtc,
            EndUtc: appt.EndUtc,
            CustomerId: customerId), ct);
    }

    /// <summary>Randevu onayı WhatsApp template'i gönderir + MessageLog'a yazar. Hata çağrıyı düşürmez.</summary>
    private static async Task SendAppointmentNotificationAsync(
        Guid tenantId, CreateAppointmentRequest req, AppDbContext db,
        IMessagingProvider messaging, IConfiguration config, CancellationToken ct)
    {
        var template = config["WHATSAPP_TEMPLATE_APPOINTMENT"] ?? "randevu_onayi";
        var parameters = new[] { req.CustomerName, req.Date, req.Time };

        var log = new MessageLog
        {
            TenantId = tenantId,
            Channel = messaging.Channel,
            ToPhone = req.CustomerPhone,
            Template = template,
        };

        var result = await messaging.SendTemplateAsync(req.CustomerPhone, template, parameters, ct);
        log.Status = result.Success ? MessageStatus.Sent : MessageStatus.Failed;
        log.ProviderMessageId = result.ProviderMessageId;
        log.Error = result.Error;

        db.MessageLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest req, AppDbContext db, ICrmSink crm, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TenantId, out var tenantId))
            return Results.BadRequest("Geçersiz tenant");

        var order = new Order
        {
            TenantId = tenantId,
            Items = req.Items,
            CustomerName = req.CustomerName,
            CustomerPhone = req.CustomerPhone,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // CRM aynalama: siparişi telesekreter kaynaklı lead olarak aktar (best-effort).
        if (crm.Enabled)
        {
            var (firstName, lastName) = SplitName(req.CustomerName);
            await crm.MirrorLeadAsync(new CrmLead(
                Name: firstName,
                Surname: lastName,
                Phone: req.CustomerPhone,
                Email: null,
                Company: null,
                Notes: $"Telesekreter sipariş: {req.Items}"), ct);
        }

        return Results.Ok(new { orderId = order.Id });
    }

    /// <summary>Tam adı Ad/Soyad olarak böler (ilk kelime Ad, kalanı Soyad). CRM lead'i Ad ister.</summary>
    private static (string FirstName, string? LastName) SplitName(string fullName)
    {
        var parts = (fullName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("Bilinmiyor", null);
        return parts.Length == 1 ? (parts[0], null) : (parts[0], parts[1]);
    }

    private static async Task<IResult> RecordCallEvent(
        CallEventRequest req, AppDbContext db, ICrmSink crm, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TenantId, out var tenantId))
            return Results.BadRequest("Geçersiz tenant");

        CallLog? startedLog = null;
        if (req.Event == "call_started")
        {
            startedLog = new CallLog
            {
                TenantId = tenantId,
                Did = req.Did,
                CustomerPhone = req.CustomerPhone,
            };
            db.CallLogs.Add(startedLog);
            if (req.Consent is not null)
                db.Consents.Add(new Consent
                {
                    TenantId = tenantId,
                    CustomerPhone = req.CustomerPhone ?? "",
                    Type = ConsentType.CallRecording,
                    Source = "call_announcement",
                });

            // CRM aynalama: çağrıyı aktivite (Arama) olarak zaman tüneline aktar (best-effort).
            if (crm.Enabled)
            {
                var customerId = await crm.FindCustomerIdByPhoneAsync(req.CustomerPhone, ct);
                await crm.MirrorActivityAsync(new CrmActivity(
                    Type: 0, // AktiviteTip.Arama
                    Title: "Telesekreter çağrısı",
                    Description: req.CustomerPhone is null ? null : $"Arayan: {req.CustomerPhone}",
                    Date: DateTime.UtcNow,
                    CustomerId: customerId,
                    LeadId: null), ct);
            }
        }
        else if (req.Event == "call_ended")
        {
            // callLogId varsa doğrudan o kayda yapış (eşzamanlı aynı-DID çağrıda doğru kayıt);
            // yoksa geriye dönük uyumluluk için latest-open lookup.
            CallLog? log;
            if (Guid.TryParse(req.CallLogId, out var callLogId))
                log = await db.CallLogs
                    .FirstOrDefaultAsync(c => c.Id == callLogId && c.TenantId == tenantId, ct);
            else
                log = await db.CallLogs
                    .Where(c => c.TenantId == tenantId && c.Did == req.Did && c.EndedAt == null)
                    .OrderByDescending(c => c.StartedAt)
                    .FirstOrDefaultAsync(ct);
            if (log is not null)
            {
                log.EndedAt = DateTime.UtcNow;
                log.TranscriptUrl = req.TranscriptUrl;
                log.RecordingUrl = req.RecordingUrl;

                // Analitik (veri akışı): süre türet + worker'ın bildirdiği metrikleri yaz.
                log.DurationSeconds = (int)(log.EndedAt.Value - log.StartedAt).TotalSeconds;
                log.EndReason = req.EndReason;
                if (req.ToolCallCount is not null) log.ToolCallCount = req.ToolCallCount.Value;
                log.Outcome = req.Outcome;

                // Transkript: tüm diyalog turlarını CallLog'a bağlı kaydet.
                if (req.Transcript is not null)
                    foreach (var turn in req.Transcript)
                        db.ConversationTurns.Add(new ConversationTurn
                        {
                            CallLogId = log.Id,
                            Role = turn.Role,
                            Text = turn.Text,
                            OccurredAt = turn.OccurredAt ?? DateTime.UtcNow,
                        });
            }
        }

        await db.SaveChangesAsync(ct);
        // call_started → callLogId döndür (worker call_ended'de geri gönderir).
        return startedLog is not null
            ? Results.Ok(new CallEventResponse(startedLog.Id.ToString()))
            : Results.Ok();
    }
}
