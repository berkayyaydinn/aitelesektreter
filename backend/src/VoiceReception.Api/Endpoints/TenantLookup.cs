using Microsoft.EntityFrameworkCore;
using VoiceReception.Api.Data;
using VoiceReception.Api.Domain;

namespace VoiceReception.Api.Endpoints;

/// <summary>DID → tenant çözümü. Hem voice worker internal API'si hem Netsantral webhook'u aynı
/// sorguyu paylaşır (DRY). Tenant + hizmetler + çalışma saatleri include edilir.</summary>
public static class TenantLookup
{
    /// <summary>Verilen DID'e bağlı PhoneNumber'ı (Tenant + Services + BusinessHours dahil) döndürür.
    /// Bulunamazsa null. Soft-delete edilmiş tenant için Tenant null döner (routing durur — beklenen).</summary>
    public static Task<PhoneNumber?> FindByDidAsync(AppDbContext db, string did, CancellationToken ct) =>
        db.PhoneNumbers
            .Include(p => p.Tenant)!.ThenInclude(t => t!.Services)
            .Include(p => p.Tenant)!.ThenInclude(t => t!.BusinessHours)
            .AsSplitQuery()   // iki collection include — kartezyen patlamayı önle
            .FirstOrDefaultAsync(p => p.Did == did, ct);

    /// <summary>Netsantral çağrılan numarayı farklı formatta iletebilir ("850..", "0850..", "90850..").
    /// PhoneNumbers.Did depolanan formatla eşleşene kadar aday formları sırayla dener.</summary>
    public static async Task<PhoneNumber?> FindByDidCandidatesAsync(
        AppDbContext db, string rawDid, CancellationToken ct)
    {
        foreach (var candidate in DidCandidates(rawDid))
        {
            var phone = await FindByDidAsync(db, candidate, ct);
            if (phone?.Tenant is not null) return phone;
        }
        return null;
    }

    /// <summary>Ham numaradan olası DID formlarını üretir (öncelik sırası, tekrarsız).</summary>
    public static IEnumerable<string> DidCandidates(string rawDid)
    {
        var raw = (rawDid ?? string.Empty).Trim();
        if (raw.Length == 0) yield break;

        var seen = new HashSet<string>();
        string Norm(string s) => s;

        // Ham değer, +90/90 soyulmuş, 0 eklenmiş, 0 soyulmuş formlar.
        var stripped = raw;
        if (stripped.StartsWith('+')) stripped = stripped[1..];
        if (stripped.StartsWith("90")) stripped = stripped[2..];

        foreach (var c in new[]
        {
            raw,
            stripped,
            stripped.StartsWith('0') ? stripped : "0" + stripped,
            stripped.TrimStart('0'),
        })
        {
            if (!string.IsNullOrEmpty(c) && seen.Add(c))
                yield return Norm(c);
        }
    }
}
