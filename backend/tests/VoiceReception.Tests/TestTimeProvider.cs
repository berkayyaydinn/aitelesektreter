namespace VoiceReception.Tests;

/// <summary>Sabit "şimdi" döndüren TimeProvider — saat-bağımlı testleri deterministik kılar.</summary>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset now) => _now = now;

    public TestTimeProvider(DateTime utc) => _now = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => _now;
}
