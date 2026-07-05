using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Outbound;

/// <summary>Dry-run dialer — gerçek arama yok, log'a yazar. İYS/saat kapısı + akış lokal test edilir.
/// OUTBOUND_DIALER=console (varsayılan). Gerçek arama için LiveKitOutboundDialer eklenir.</summary>
public class ConsoleOutboundDialer : IOutboundDialer
{
    private readonly ILogger<ConsoleOutboundDialer> _logger;

    public ConsoleOutboundDialer(ILogger<ConsoleOutboundDialer> logger) => _logger = logger;

    public Task<DialResult> PlaceCallAsync(Campaign campaign, CampaignTarget target, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DRY-RUN arama] kampanya='{Campaign}' -> {Name} <{Phone}> | script='{Script}'",
            campaign.Name, target.CustomerName, target.CustomerPhone, campaign.ScriptPrompt);

        return Task.FromResult(new DialResult(true, null));
    }
}
