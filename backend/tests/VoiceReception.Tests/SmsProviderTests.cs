using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReception.Api.Messaging.Sms;
using Xunit;

namespace VoiceReception.Tests;

/// <summary>SMS sağlayıcı birim testleri — console dry-run + Netgsm URL/yanıt/normalize.</summary>
public class SmsProviderTests
{
    [Fact]
    public async Task ConsoleSmsProvider_returns_success_and_id()
    {
        var provider = new ConsoleSmsProvider(NullLogger<ConsoleSmsProvider>.Instance);
        var result = await provider.SendAsync("+905551112233", "Randevu hatırlatması");
        Assert.True(result.Success);
        Assert.StartsWith("dryrun-sms-", result.ProviderMessageId);
        Assert.Equal("console-sms", provider.Channel);
    }

    [Fact]
    public async Task NetgsmSmsProvider_fails_when_credentials_missing()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new NetgsmSmsProvider(new HttpClient(), config);
        var result = await provider.SendAsync("+905551112233", "test");
        Assert.False(result.Success);
        Assert.Contains("yapılandırılmamış", result.Error);
        Assert.Equal("sms", provider.Channel);
    }

    [Fact]
    public async Task NetgsmSmsProvider_sends_and_parses_jobid_and_builds_query()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NETGSM_USERCODE"] = "8503021234",
            ["NETGSM_PASSWORD"] = "secret",
            ["NETGSM_MSGHEADER"] = "TESTBASLIK",
        }).Build();

        var handler = new StubHandler("00 1234567");
        var provider = new NetgsmSmsProvider(new HttpClient(handler), config);
        var result = await provider.SendAsync("0555 111 22 33", "Merhaba");

        Assert.True(result.Success);
        Assert.Equal("1234567", result.ProviderMessageId);
        Assert.Contains("usercode=8503021234", handler.LastUri);
        Assert.Contains("gsmno=905551112233", handler.LastUri);   // normalize: 0 -> 90, + yok
        Assert.Contains("msgheader=TESTBASLIK", handler.LastUri);
    }

    [Fact]
    public async Task NetgsmSmsProvider_maps_error_code()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NETGSM_USERCODE"] = "u",
            ["NETGSM_PASSWORD"] = "p",
        }).Build();

        var provider = new NetgsmSmsProvider(new HttpClient(new StubHandler("30")), config);
        var result = await provider.SendAsync("+905551112233", "test");

        Assert.False(result.Success);
        Assert.Contains("30", result.Error);
    }

    [Theory]
    [InlineData("+905551112233", "905551112233")]
    [InlineData("0555 111 22 33", "905551112233")]
    [InlineData("905551112233", "905551112233")]
    [InlineData("5551112233", "905551112233")]
    public void NetgsmSmsProvider_normalizes_phone(string input, string expected)
        => Assert.Equal(expected, NetgsmSmsProvider.NormalizePhone(input));

    [Fact]
    public void ParseResponse_treats_empty_body_as_failure()
    {
        var r = NetgsmSmsProvider.ParseResponse("");
        Assert.False(r.Success);
    }

    /// <summary>Netgsm metin yanıtını taklit eden + istek URI'sini yakalayan HTTP handler.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string? LastUri { get; private set; }

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
            });
        }
    }
}
