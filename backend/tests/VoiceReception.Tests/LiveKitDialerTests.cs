using Microsoft.Extensions.Configuration;
using VoiceReception.Api.Domain;
using VoiceReception.Api.Outbound;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>LiveKitOutboundDialer birim testleri — kimlik-yok yolu, E.164, istek gövdesi.</summary>
public class LiveKitDialerTests
{
    [Fact]
    public async Task Fails_when_credentials_missing()
    {
        var dialer = new LiveKitOutboundDialer(new HttpClient(), new ConfigurationBuilder().Build(), TimeProvider.System);
        var result = await dialer.PlaceCallAsync(
            new Campaign { Name = "K" },
            new CampaignTarget { CustomerPhone = "+905551112233", CustomerName = "Ali" });
        Assert.False(result.Success);
        Assert.Contains("kimlik", result.Reason);
    }

    [Theory]
    [InlineData("0555 111 22 33", "+905551112233")]
    [InlineData("905551112233", "+905551112233")]
    [InlineData("5551112233", "+905551112233")]
    public void ToE164_normalizes(string input, string expected)
        => Assert.Equal(expected, LiveKitOutboundDialer.ToE164(input));

    [Fact]
    public async Task Places_call_with_trunk_and_callto_in_body()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LIVEKIT_URL"] = "wss://lk.example.com",
            ["LIVEKIT_API_KEY"] = "key",
            ["LIVEKIT_API_SECRET"] = "supersecretsupersecret",
            ["NETGSM_SIP_OUTBOUND_TRUNK_ID"] = "ST_abc123",
        }).Build();

        var handler = new CapturingHandler("{\"participantId\":\"PA_1\"}");
        var dialer = new LiveKitOutboundDialer(new HttpClient(handler), config, TimeProvider.System);

        var result = await dialer.PlaceCallAsync(
            new Campaign { Name = "Yaz Kampanyası", ScriptPrompt = "Merhaba" },
            new CampaignTarget { CustomerPhone = "05551112233", CustomerName = "Ali" });

        Assert.True(result.Success);
        Assert.Contains("ST_abc123", handler.LastBody);
        Assert.Contains("905551112233", handler.LastBody);  // JSON '+'ı +'a kaçırır; rakamlar yeterli
        Assert.Contains("/twirp/livekit.SIP/CreateSIPParticipant", handler.LastUri);
        Assert.StartsWith("Bearer ", handler.LastAuth);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _json;
        public string? LastBody { get; private set; }
        public string? LastUri { get; private set; }
        public string? LastAuth { get; private set; }

        public CapturingHandler(string json) => _json = json;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri?.ToString();
            LastAuth = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(_json) };
        }
    }
}
