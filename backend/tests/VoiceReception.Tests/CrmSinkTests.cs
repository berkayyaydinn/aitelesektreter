using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReception.Api.Crm;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>CRM sink birim testleri — no-op sink + Mirbal HTTP istemcisi (payload, header, hata yutma).</summary>
public class CrmSinkTests
{
    [Fact]
    public void NullCrmSink_is_disabled()
    {
        var sink = new NullCrmSink();
        Assert.False(sink.Enabled);
    }

    [Fact]
    public async Task NullCrmSink_find_customer_returns_null_and_mirrors_noop()
    {
        var sink = new NullCrmSink();
        Assert.Null(await sink.FindCustomerIdByPhoneAsync("+905551112233"));
        // No-op: hata fırlatmamalı.
        await sink.MirrorAppointmentAsync(new CrmAppointment("b", "a", DateTime.UtcNow, DateTime.UtcNow, null));
        await sink.MirrorLeadAsync(new CrmLead("Ali", null, null, null, null, null));
        await sink.MirrorActivityAsync(new CrmActivity(0, "t", null, null, null, null));
    }

    [Fact]
    public async Task MirbalCrmSink_find_customer_parses_first_match()
    {
        var handler = new RecordingHandler(@"[{""id"":42,""ad"":""Ali""},{""id"":7,""ad"":""Veli""}]");
        var sink = NewSink(handler);

        var id = await sink.FindCustomerIdByPhoneAsync("+905551112233");

        Assert.Equal(42, id);
        Assert.Contains("/api/telesekreter/musteri/ara", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task MirbalCrmSink_find_customer_returns_null_on_empty()
    {
        var sink = NewSink(new RecordingHandler("[]"));
        Assert.Null(await sink.FindCustomerIdByPhoneAsync("+905551112233"));
    }

    [Fact]
    public async Task MirbalCrmSink_find_customer_returns_null_on_blank_phone_without_calling()
    {
        var handler = new RecordingHandler("[]");
        var sink = NewSink(handler);

        Assert.Null(await sink.FindCustomerIdByPhoneAsync("  "));
        Assert.Null(handler.LastRequest); // boş telefon -> HTTP yapılmaz
    }

    [Fact]
    public async Task MirbalCrmSink_mirror_appointment_posts_expected_payload()
    {
        var handler = new RecordingHandler(@"{""id"":1}");
        var sink = NewSink(handler);

        await sink.MirrorAppointmentAsync(new CrmAppointment(
            "Telesekreter randevu", "Telefon: x",
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc),
            CustomerId: 42));

        Assert.EndsWith("/api/telesekreter/randevu", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("baslangicZamani", handler.LastBody);
        Assert.Contains("\"musteriId\":42", handler.LastBody);
    }

    [Fact]
    public async Task MirbalCrmSink_mirror_lead_posts_expected_payload()
    {
        var handler = new RecordingHandler(@"{""id"":1}");
        var sink = NewSink(handler);

        await sink.MirrorLeadAsync(new CrmLead("Ali", "Veli", "+905551112233", null, null, "sipariş: 2 kahve"));

        Assert.EndsWith("/api/telesekreter/lead", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"ad\":\"Ali\"", handler.LastBody);
        Assert.Contains("sipari", handler.LastBody);
    }

    [Fact]
    public async Task MirbalCrmSink_sends_api_key_header()
    {
        var handler = new RecordingHandler(@"{""id"":1}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://crm.test") };
        http.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");
        var sink = new MirbalCrmSink(http, NullLogger<MirbalCrmSink>.Instance);

        await sink.MirrorActivityAsync(new CrmActivity(0, "çağrı", null, DateTime.UtcNow, null, null));

        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-Api-Key", out var vals));
        Assert.Equal("secret-key", string.Join("", vals!));
    }

    [Fact]
    public async Task MirbalCrmSink_swallows_http_error()
    {
        var sink = NewSink(new RecordingHandler("boom", HttpStatusCode.InternalServerError));
        // Hata fırlatmamalı (best-effort).
        await sink.MirrorAppointmentAsync(new CrmAppointment("b", null, DateTime.UtcNow, DateTime.UtcNow, null));
    }

    [Fact]
    public async Task MirbalCrmSink_swallows_transport_exception()
    {
        var sink = NewSink(new ThrowingHandler());
        await sink.MirrorLeadAsync(new CrmLead("Ali", null, null, null, null, null));
        Assert.Null(await sink.FindCustomerIdByPhoneAsync("+905551112233"));
    }

    private static MirbalCrmSink NewSink(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://crm.test") },
            NullLogger<MirbalCrmSink>.Instance);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";

        public RecordingHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("ağ hatası");
    }
}
