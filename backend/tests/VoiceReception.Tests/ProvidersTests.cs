using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReception.Api.Compliance;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Invoicing;
using VoiceReception.Api.Messaging;
using VoiceReception.Api.Outbound;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>Sağlayıcı/yardımcı birim testleri — dry-run dialer, WhatsApp token-yok yolu, TurkeyTime.</summary>
public class ProvidersTests
{
    [Fact]
    public async Task ConsoleOutboundDialer_returns_success()
    {
        var dialer = new ConsoleOutboundDialer(NullLogger<ConsoleOutboundDialer>.Instance);
        var result = await dialer.PlaceCallAsync(
            new Campaign { Name = "K" },
            new CampaignTarget { CustomerPhone = "+905551112233", CustomerName = "Ali" });
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ConsoleMessagingProvider_returns_success_and_id()
    {
        var provider = new ConsoleMessagingProvider(NullLogger<ConsoleMessagingProvider>.Instance);
        var result = await provider.SendTemplateAsync("+905551112233", "randevu_onayi", new[] { "Ali" });
        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.ProviderMessageId));
        Assert.Equal("console", provider.Channel);
    }

    [Fact]
    public async Task WhatsAppCloudProvider_fails_when_token_missing()
    {
        var config = new ConfigurationBuilder().Build(); // boş -> token yok
        var provider = new WhatsAppCloudProvider(new HttpClient(), config);
        var result = await provider.SendTemplateAsync("+905551112233", "randevu_onayi", new[] { "Ali" });
        Assert.False(result.Success);
        Assert.Contains("token", result.Error);
        Assert.Equal("whatsapp", provider.Channel);
    }

    [Fact]
    public async Task WhatsAppCloudProvider_sends_and_parses_message_id()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WHATSAPP_PHONE_NUMBER_ID"] = "12345",
            ["WHATSAPP_ACCESS_TOKEN"] = "tok",
        }).Build();

        var handler = new StubHandler(@"{""messages"":[{""id"":""wamid.ABC123""}]}");
        var provider = new WhatsAppCloudProvider(new HttpClient(handler), config);
        var result = await provider.SendTemplateAsync("+905551112233", "randevu_onayi", new[] { "Ali", "2026-06-15" });

        Assert.True(result.Success);
        Assert.Equal("wamid.ABC123", result.ProviderMessageId);
        Assert.Contains("Bearer tok", handler.LastAuthHeader);
    }

    [Fact]
    public async Task ConsoleInvoiceProvider_returns_success_with_id()
    {
        var provider = new ConsoleInvoiceProvider(NullLogger<ConsoleInvoiceProvider>.Instance);
        var result = await provider.IssueAsync(new Invoice
        {
            CustomerName = "Ali", Amount = 500m, Description = "Danışmanlık",
        });
        Assert.True(result.Success);
        Assert.StartsWith("dryrun-inv-", result.ProviderInvoiceId);
    }

    [Fact]
    public void TurkeyTime_is_utc_plus_three()
    {
        var delta = TurkeyTime.Now() - DateTime.UtcNow;
        Assert.InRange(delta.TotalHours, 2.9, 3.1);
    }

    /// <summary>WhatsApp Graph API yanıtını taklit eden HTTP handler.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        public string? LastAuthHeader { get; private set; }

        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastAuthHeader = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_json),
            });
        }
    }
}
