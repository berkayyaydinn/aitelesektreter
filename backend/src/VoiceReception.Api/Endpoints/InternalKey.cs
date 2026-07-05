namespace VoiceReception.Api.Endpoints;

/// <summary>X-Internal-Key endpoint filtresi + opsiyonel tenant kapsamı.
///
/// İki anahtar türü:
///  - <b>Master</b> (INTERNAL_API_KEY): kısıtsız. Voice worker tüm tenant'lar için çalışır (tek worker,
///    DID→tenant backend'den). CRM admin de bunu kullanabilir.
///  - <b>Scoped</b> (INTERNAL_TENANT_KEYS = "tenantGuid:key,tenantGuid:key"): yalnız kendi tenant'ının
///    route'una erişir. CRM çok-tenant'a açılınca her işletmeye ayrı anahtar verilebilir (defense-in-depth).
///
/// Yapılandırılmazsa davranış eskisiyle aynı (sadece master key). RequireTenantScope filtresi,
/// route'ta {tenantId} olan grup'larda scoped anahtarın başka tenant'a dokunmasını 403 ile engeller.
/// </summary>
public static class InternalKey
{
    /// <summary>Scoped anahtarın bağlı olduğu tenantId (master ise yok). HttpContext.Items anahtarı.</summary>
    public const string ScopedTenantItem = "ScopedTenantId";

    public static async ValueTask<object?> Filter(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var provided = ctx.HttpContext.Request.Headers["X-Internal-Key"].ToString();
        if (string.IsNullOrEmpty(provided))
            return Results.Unauthorized();

        var master = config["INTERNAL_API_KEY"];
        if (!string.IsNullOrEmpty(master) && provided == master)
        {
            ctx.HttpContext.Items[ScopedTenantItem] = null; // kısıtsız
            return await next(ctx);
        }

        var scoped = ResolveScopedTenant(config["INTERNAL_TENANT_KEYS"], provided);
        if (scoped is Guid tenantId)
        {
            ctx.HttpContext.Items[ScopedTenantItem] = tenantId;
            return await next(ctx);
        }

        return Results.Unauthorized();
    }

    /// <summary>route {tenantId} ile scoped anahtarın tenant'ı uyuşmazsa 403. Master key'de no-op.</summary>
    public static async ValueTask<object?> RequireTenantScope(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (ctx.HttpContext.Items.TryGetValue(ScopedTenantItem, out var raw) && raw is Guid scopedTenant)
        {
            var routeTenant = ctx.HttpContext.Request.RouteValues["tenantId"]?.ToString();
            if (!Guid.TryParse(routeTenant, out var rid) || rid != scopedTenant)
                return Results.Json(new { error = "Bu tenant'a erişim yetkisi yok" }, statusCode: 403);
        }
        return await next(ctx);
    }

    /// <summary>"guid:key,guid:key" çözer; verilen key eşleşirse o tenantId'yi döndürür.</summary>
    private static Guid? ResolveScopedTenant(string? tenantKeys, string provided)
    {
        if (string.IsNullOrWhiteSpace(tenantKeys)) return null;
        foreach (var pair in tenantKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = pair.IndexOf(':');
            if (idx <= 0 || idx == pair.Length - 1) continue;
            var guidPart = pair[..idx].Trim();
            var keyPart = pair[(idx + 1)..].Trim();
            if (keyPart == provided && Guid.TryParse(guidPart, out var tid))
                return tid;
        }
        return null;
    }
}
