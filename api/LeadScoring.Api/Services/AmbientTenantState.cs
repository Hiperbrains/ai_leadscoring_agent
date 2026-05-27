namespace LeadScoring.Api.Services;

/// <summary>
/// Scoped override that lets background work (created via <see cref="IServiceScopeFactory"/>
/// outside an HTTP request) carry the originating request's tenant + product identity.
/// <para>
/// <see cref="TenantContext"/>, <see cref="ProductContext"/>, and <see cref="TenantLeadScope"/>
/// consult this first. If nothing is set the services fall back to claims / headers on
/// <see cref="IHttpContextAccessor.HttpContext"/> as before.
/// </para>
/// </summary>
public interface IAmbientTenantState
{
    bool HasOverride { get; }
    string? CompanyName { get; }
    string? SchemaName { get; }
    Guid? TenantId { get; }
    int? ProductId { get; }

    void Set(string? companyName, string? schemaName, Guid? tenantId, int? productId);
}

public class AmbientTenantState : IAmbientTenantState
{
    public bool HasOverride { get; private set; }
    public string? CompanyName { get; private set; }
    public string? SchemaName { get; private set; }
    public Guid? TenantId { get; private set; }
    public int? ProductId { get; private set; }

    public void Set(string? companyName, string? schemaName, Guid? tenantId, int? productId)
    {
        CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        SchemaName = string.IsNullOrWhiteSpace(schemaName) ? null : schemaName.Trim();
        TenantId = tenantId;
        ProductId = productId;
        HasOverride =
            CompanyName is not null
            || SchemaName is not null
            || TenantId.HasValue
            || ProductId.HasValue;
    }
}
