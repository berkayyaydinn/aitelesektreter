namespace VoiceReception.Api.Retention;

/// <summary>Saklama/imha ortak sabitleri — sweeper ve DSAR ucu aynı işaretleyiciyi kullanır.</summary>
public static class RetentionDefaults
{
    /// <summary>Anonimleştirilen PII alanlarına yazılan işaret. Satır kalır (istatistik/denetim),
    /// kişisel veri gider.</summary>
    public const string Anonymized = "[silindi]";
}
